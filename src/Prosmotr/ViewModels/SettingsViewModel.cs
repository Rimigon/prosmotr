using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Prosmotr.Infrastructure;
using Prosmotr.Models;
using Prosmotr.Services.Abstractions;

namespace Prosmotr.ViewModels;

/// <summary>ViewModel окна настроек. Любое изменение сразу сохраняется и применяется.</summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly IThemeService _theme;
    private readonly IFileAssociationService _assoc;
    private readonly IRecentFilesService _recent;

    private bool _loading;

    public IReadOnlyList<AppTheme> Themes { get; } = new[] { AppTheme.System, AppTheme.Light, AppTheme.Dark };
    public IReadOnlyList<float> PlaybackRates { get; } =
        new[] { 0.25f, 0.5f, 0.75f, 1f, 1.25f, 1.5f, 1.75f, 2f, 2.5f, 3f, 4f, 5f };
    public IReadOnlyList<ThumbnailStripPosition> StripPositions { get; } =
        new[] { ThumbnailStripPosition.Bottom, ThumbnailStripPosition.Left };

    // Навигационные и критичные клавиши, которые нельзя назначать на закрытие/скрытие UI.
    // Включают жёстко зашитые в MainWindow.TryHandleHotkey: F (полный экран), F12 (клон),
    // M (mute), Delete/Decimal (удаление) — иначе настройка перекрыла бы зашитое действие.
    private static readonly HashSet<string> ConflictingKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Delete", "Decimal", "Left", "Right", "Up", "Down", "Space", "Escape",
        "OemOpenBrackets", "OemCloseBrackets", "OemPlus", "OemMinus", "Add", "Subtract",
        "F", "F12", "M"
    };

    // Мастер-список клавиш, которые можно назначать на пользовательские действия.
    // F, F12, M, Delete/Decimal и навигационные клавиши исключены — они жёстко зашиты в MainWindow.
    private static readonly string[] AllConfigurableKeys = new[]
    {
        "PageDown", "PageUp", "End", "Home", "Insert",
        "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11",
        "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P",
        "A", "S", "D", "G", "H", "J", "K", "L",
        "Z", "X", "C", "V", "B", "N"
    };

    /// <summary>Доступные клавиши для "Закрыть программу" — без уже занятых другими настройками.</summary>
    public IReadOnlyList<string> ExitKeyItems => AllConfigurableKeys
        .Where(k => k != FullScreenKey && k != ToggleChromeKey)
        .ToList();

    /// <summary>Доступные клавиши для "Скрыть/показать панель" — без уже занятых другими настройками.</summary>
    public IReadOnlyList<string> ChromeKeyItems => AllConfigurableKeys
        .Where(k => k != FullScreenKey && k != ExitKey)
        .ToList();

    /// <summary>Доступные клавиши для "Полный экран" — без уже занятых другими настройками.</summary>
    public IReadOnlyList<string> FullScreenKeyItems => AllConfigurableKeys
        .Where(k => k != ExitKey && k != ToggleChromeKey)
        .ToList();

    [ObservableProperty] private AppTheme _theme_;
    [ObservableProperty] private float _defaultPlaybackRate;
    [ObservableProperty] private bool _rememberRatePerFile;
    [ObservableProperty] private bool _resumeVideoPosition;
    [ObservableProperty] private bool _confirmDelete;
    [ObservableProperty] private bool _permanentDelete;
    [ObservableProperty] private bool _showDeleteNotification;
    [ObservableProperty] private bool _openLastOnStartup;
    [ObservableProperty] private bool _clearRecentOnStartup;
    [ObservableProperty] private bool _showThumbnails;
    [ObservableProperty] private ThumbnailStripPosition _thumbnailStripPosition;
    [ObservableProperty] private bool _autoHideControls;
    [ObservableProperty] private int _slideshowIntervalSeconds;
    [ObservableProperty] private int _seekStepSeconds;
    [ObservableProperty] private bool _frameByFrameSeek;
    [ObservableProperty] private bool _arrowKeysSeekVideo;
    [ObservableProperty] private bool _matchExplorerSort;
    [ObservableProperty] private bool _integrateShell;
    [ObservableProperty] private bool _isAssociationsRegistered;
    [ObservableProperty] private string _exitKey = "End";
    [ObservableProperty] private string _toggleChromeKey = "PageDown";
    [ObservableProperty] private string _fullScreenKey = "F11";

    public SettingsViewModel(
        ISettingsService settings,
        IThemeService theme,
        IFileAssociationService assoc,
        IRecentFilesService recent)
    {
        _settings = settings;
        _theme = theme;
        _assoc = assoc;
        _recent = recent;
        LoadFromSettings();
    }

    private void LoadFromSettings()
    {
        _loading = true;
        var s = _settings.Settings;
        Theme_ = s.Theme;
        DefaultPlaybackRate = s.DefaultPlaybackRate;
        RememberRatePerFile = s.RememberRatePerFile;
        ResumeVideoPosition = s.ResumeVideoPosition;
        ConfirmDelete = s.ConfirmDelete;
        PermanentDelete = s.PermanentDelete;
        ShowDeleteNotification = s.ShowDeleteNotification;
        OpenLastOnStartup = s.OpenLastOnStartup;
        ClearRecentOnStartup = s.ClearRecentOnStartup;
        ShowThumbnails = s.ShowThumbnails;
        ThumbnailStripPosition = s.ThumbnailStripPosition;
        AutoHideControls = s.AutoHideControls;
        SlideshowIntervalSeconds = s.SlideshowIntervalSeconds;
        SeekStepSeconds = s.SeekStepSeconds;
        FrameByFrameSeek = s.FrameByFrameSeek;
        ArrowKeysSeekVideo = s.ArrowKeysSeekVideo;
        MatchExplorerSort = s.MatchExplorerSort;
        IntegrateShell = s.IntegrateShell;
        ExitKey = IsAllowedKey(s.ExitKey) ? s.ExitKey : "End";
        ToggleChromeKey = IsAllowedKey(s.ToggleChromeKey) ? s.ToggleChromeKey : "PageDown";
        FullScreenKey = IsAllowedKey(s.FullScreenKey) ? s.FullScreenKey : "F11";
        ResolveKeyConflicts();
        IsAssociationsRegistered = _assoc.IsRegistered;
        _loading = false;
    }

    private static bool IsAllowedKey(string key)
        => !ConflictingKeys.Contains(key) && Enum.TryParse<System.Windows.Input.Key>(key, out _);

    /// <summary>Защита от ручного редактирования settings.json: ни одна клавиша не может быть
    /// назначена на два действия одновременно. Конфликтующая настройка сбрасывается к свободному
    /// значению (дефолту, если он не занят, иначе — первой свободной клавише из списка).</summary>
    private void ResolveKeyConflicts()
    {
        if (ExitKey == FullScreenKey || ToggleChromeKey == FullScreenKey)
            FullScreenKey = FindFreeKey(new AppSettings().FullScreenKey, ExitKey, ToggleChromeKey) ?? "F11";
        if (ExitKey == ToggleChromeKey)
            ToggleChromeKey = FindFreeKey(new AppSettings().ToggleChromeKey, ExitKey, FullScreenKey) ?? "PageDown";
    }

    private static string? FindFreeKey(string preferred, string reserved1, string reserved2)
    {
        if (preferred != reserved1 && preferred != reserved2) return preferred;
        return AllConfigurableKeys.FirstOrDefault(k => k != reserved1 && k != reserved2);
    }

    private void Commit(bool immediate = false)
    {
        if (_loading) return;
        var s = _settings.Settings;
        s.Theme = Theme_;
        s.DefaultPlaybackRate = DefaultPlaybackRate;
        s.RememberRatePerFile = RememberRatePerFile;
        s.ResumeVideoPosition = ResumeVideoPosition;
        s.ConfirmDelete = ConfirmDelete;
        s.PermanentDelete = PermanentDelete;
        s.ShowDeleteNotification = ShowDeleteNotification;
        s.OpenLastOnStartup = OpenLastOnStartup;
        s.ClearRecentOnStartup = ClearRecentOnStartup;
        s.ShowThumbnails = ShowThumbnails;
        s.ThumbnailStripPosition = ThumbnailStripPosition;
        s.AutoHideControls = AutoHideControls;
        s.SlideshowIntervalSeconds = Math.Clamp(SlideshowIntervalSeconds, 1, 60);
        s.SeekStepSeconds = Math.Clamp(SeekStepSeconds, 1, 30); // согласовано с [Range] и слайдером
        s.FrameByFrameSeek = FrameByFrameSeek;
        s.ArrowKeysSeekVideo = ArrowKeysSeekVideo;
        s.MatchExplorerSort = MatchExplorerSort;
        s.IntegrateShell = IntegrateShell;
        s.ExitKey = ExitKey;
        s.ToggleChromeKey = ToggleChromeKey;
        s.FullScreenKey = FullScreenKey;
        if (immediate)
            _settings.Save(); // поднимет SettingsChanged → тема/раскладка применятся вживую
        else
            _settings.SaveDebounced();
    }

    // Любое изменение фиксируем в настройках.
    partial void OnTheme_Changed(AppTheme value) { Commit(immediate: true); if (!_loading) _theme.Apply(value); }
    partial void OnDefaultPlaybackRateChanged(float value) => Commit();
    partial void OnRememberRatePerFileChanged(bool value) => Commit();
    partial void OnResumeVideoPositionChanged(bool value) => Commit();
    partial void OnConfirmDeleteChanged(bool value) => Commit();
    partial void OnPermanentDeleteChanged(bool value) => Commit();
    partial void OnShowDeleteNotificationChanged(bool value) => Commit();
    partial void OnOpenLastOnStartupChanged(bool value) => Commit();
    partial void OnClearRecentOnStartupChanged(bool value) => Commit();
    // immediate: true для «живых» настроек — Save() поднимает SettingsChanged, и MainViewModel
    // применяет их сразу (показ/положение ленты, интервал слайд-шоу). SaveDebounced событие НЕ
    // поднимает (тихий путь для частых volume/recent), поэтому эти изменения иначе не применялись бы.
    partial void OnShowThumbnailsChanged(bool value) => Commit(immediate: true);
    partial void OnThumbnailStripPositionChanged(ThumbnailStripPosition value) => Commit(immediate: true);
    partial void OnAutoHideControlsChanged(bool value) => Commit();
    partial void OnSlideshowIntervalSecondsChanged(int value) => Commit(immediate: true);
    partial void OnSeekStepSecondsChanged(int value) => Commit();
    partial void OnFrameByFrameSeekChanged(bool value) => Commit();
    partial void OnArrowKeysSeekVideoChanged(bool value) => Commit();
    partial void OnMatchExplorerSortChanged(bool value) => Commit();
    partial void OnExitKeyChanged(string value)
    {
        Commit();
        OnPropertyChanged(nameof(ExitKeyItems));
        OnPropertyChanged(nameof(ChromeKeyItems));
        OnPropertyChanged(nameof(FullScreenKeyItems));
    }

    partial void OnToggleChromeKeyChanged(string value)
    {
        Commit();
        OnPropertyChanged(nameof(ExitKeyItems));
        OnPropertyChanged(nameof(ChromeKeyItems));
        OnPropertyChanged(nameof(FullScreenKeyItems));
    }

    partial void OnFullScreenKeyChanged(string value)
    {
        Commit();
        OnPropertyChanged(nameof(ExitKeyItems));
        OnPropertyChanged(nameof(ChromeKeyItems));
        OnPropertyChanged(nameof(FullScreenKeyItems));
    }

    partial void OnIntegrateShellChanged(bool value)
    {
        Commit();
        if (_loading) return;
        // Сразу применяем: регистрируем/убираем ассоциации и контекстное меню.
        // try/catch: запись в реестр может упасть в ограниченной среде (GPO/политика) —
        // не роняем в DispatcherUnhandledException, тумблер ресинхронизируем с фактом.
        try
        {
            if (value && !_assoc.IsRegistered) _assoc.Register();
            else if (!value && _assoc.IsRegistered) _assoc.Unregister();
        }
        catch (Exception ex) { AppLog.Error("IntegrateShell toggle", ex); }
        IsAssociationsRegistered = _assoc.IsRegistered;
    }

    [RelayCommand]
    private void ToggleAssociations()
    {
        try
        {
            if (IsAssociationsRegistered) _assoc.Unregister();
            else _assoc.Register();
        }
        catch (Exception ex) { AppLog.Error("ToggleAssociations", ex); }
        IsAssociationsRegistered = _assoc.IsRegistered;
    }

    [RelayCommand] private void OpenDefaultApps() => _assoc.OpenDefaultAppsSettings();
    [RelayCommand] private void ClearRecent() => _recent.Clear();

    // Для отображения скорости в списке («1,5×»).
    public static string FormatRate(float r) => r.ToString("0.##", CultureInfo.CurrentCulture) + "×";
}
