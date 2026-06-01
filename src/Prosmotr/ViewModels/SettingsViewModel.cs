using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    public IReadOnlyList<string> ExitKeys { get; } = new[]
    {
        "PageDown", "PageUp", "End", "Home", "Insert", "Delete", "Escape",
        "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
        "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P",
        "A", "S", "D", "F", "G", "H", "J", "K", "L",
        "Z", "X", "C", "V", "B", "N", "M"
    };

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
    [ObservableProperty] private bool _matchExplorerSort;
    [ObservableProperty] private bool _integrateShell;
    [ObservableProperty] private bool _isAssociationsRegistered;
    [ObservableProperty] private string _exitKey = "End";
    [ObservableProperty] private string _toggleChromeKey = "PageDown";

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
        MatchExplorerSort = s.MatchExplorerSort;
        IntegrateShell = s.IntegrateShell;
        ExitKey = s.ExitKey;
        ToggleChromeKey = s.ToggleChromeKey;
        IsAssociationsRegistered = _assoc.IsRegistered;
        _loading = false;
    }

    private void Commit()
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
        s.SeekStepSeconds = Math.Clamp(SeekStepSeconds, 1, 120);
        s.FrameByFrameSeek = FrameByFrameSeek;
        s.MatchExplorerSort = MatchExplorerSort;
        s.IntegrateShell = IntegrateShell;
        s.ExitKey = ExitKey;
        s.ToggleChromeKey = ToggleChromeKey;
        _settings.Save(); // поднимет SettingsChanged → тема/раскладка применятся вживую
    }

    // Любое изменение фиксируем в настройках.
    partial void OnTheme_Changed(AppTheme value) { Commit(); if (!_loading) _theme.Apply(value); }
    partial void OnDefaultPlaybackRateChanged(float value) => Commit();
    partial void OnRememberRatePerFileChanged(bool value) => Commit();
    partial void OnResumeVideoPositionChanged(bool value) => Commit();
    partial void OnConfirmDeleteChanged(bool value) => Commit();
    partial void OnPermanentDeleteChanged(bool value) => Commit();
    partial void OnShowDeleteNotificationChanged(bool value) => Commit();
    partial void OnOpenLastOnStartupChanged(bool value) => Commit();
    partial void OnClearRecentOnStartupChanged(bool value) => Commit();
    partial void OnShowThumbnailsChanged(bool value) => Commit();
    partial void OnThumbnailStripPositionChanged(ThumbnailStripPosition value) => Commit();
    partial void OnAutoHideControlsChanged(bool value) => Commit();
    partial void OnSlideshowIntervalSecondsChanged(int value) => Commit();
    partial void OnSeekStepSecondsChanged(int value) => Commit();
    partial void OnFrameByFrameSeekChanged(bool value) => Commit();
    partial void OnMatchExplorerSortChanged(bool value) => Commit();
    partial void OnExitKeyChanged(string value) => Commit();
    partial void OnToggleChromeKeyChanged(string value) => Commit();

    partial void OnIntegrateShellChanged(bool value)
    {
        Commit();
        if (_loading) return;
        // Сразу применяем: регистрируем/убираем ассоциации и контекстное меню.
        if (value && !_assoc.IsRegistered) _assoc.Register();
        else if (!value && _assoc.IsRegistered) _assoc.Unregister();
        IsAssociationsRegistered = _assoc.IsRegistered;
    }

    [RelayCommand]
    private void ToggleAssociations()
    {
        if (IsAssociationsRegistered) _assoc.Unregister();
        else _assoc.Register();
        IsAssociationsRegistered = _assoc.IsRegistered;
    }

    [RelayCommand] private void OpenDefaultApps() => _assoc.OpenDefaultAppsSettings();
    [RelayCommand] private void ClearRecent() => _recent.Clear();

    // Для отображения скорости в списке («1,5×»).
    public static string FormatRate(float r) => r.ToString("0.##", CultureInfo.CurrentCulture) + "×";
}
