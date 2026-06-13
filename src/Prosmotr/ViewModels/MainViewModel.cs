using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Prosmotr.Converters;
using Prosmotr.Infrastructure;
using Prosmotr.Models;
using Prosmotr.Services;
using Prosmotr.Services.Abstractions;

namespace Prosmotr.ViewModels;

/// <summary>Главный оркестратор: открытие, навигация, удаление, полноэкран, слайд-шоу.</summary>
public sealed partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly IMediaLibraryService _library;
    private readonly INavigationService _nav;
    private readonly IFileDeletionService _deletion;
    private readonly ISettingsService _settings;
    private readonly IDialogService _dialog;
    private readonly IShellService _shell;
    private readonly IRecentFilesService _recent;
    private readonly IThemeService _theme;
    private readonly IImageCache _imageCache;
    private readonly LibVlcProvider _vlc;
    private readonly IDisplayTopologyService _displayTopology;
    private readonly IPlaybackPositionStore _positions;
    private readonly INotificationService _notify;

    public INotificationService NotificationService => _notify;

    private readonly DispatcherTimer _slideshowTimer;
    private readonly Func<MediaItem, ImageViewerViewModel> _imageVmFactory;
    private readonly Func<MediaItem, VideoViewerViewModel> _videoVmFactory;

    public ThumbnailStripViewModel ThumbnailStrip { get; }

    [ObservableProperty] private object? _currentContent;
    [ObservableProperty] private bool _hasItems;
    [ObservableProperty] private bool _isFullScreen;
    [ObservableProperty] private bool _isSlideshowActive;
    [ObservableProperty] private bool _cloneDisplayActive;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string _currentFileName = "Просмотр";

    [ObservableProperty] private SortField _selectedSortField = SortField.Name;
    [ObservableProperty] private bool _sortDescending;

    /// <summary>Видны ли «плавающие» элементы поверх контента (нижняя панель фото и боковые стрелки).
    /// Скрываются по таймеру бездействия и снова показываются при движении мыши (управляет MainWindow).</summary>
    [ObservableProperty] private bool _chromeVisible = true;

    public IReadOnlyList<SortField> SortFields { get; } = new[]
    {
        SortField.Name, SortField.DateModified, SortField.DateCreated, SortField.Size, SortField.Type
    };

    /// <summary>Просьба открыть окно настроек (обрабатывает MainWindow).</summary>
    public event Action? SettingsRequested;

    /// <summary>Просьба открыть окно свойств файла (обрабатывает MainWindow).</summary>
    public event Action<MediaItem>? PropertiesRequested;

    public bool ShowThumbnailStrip =>
        _settings.Settings.ShowThumbnails && HasItems && !IsFullScreen;

    public bool ShowNavigation => _nav.Items.Count > 1;

    /// <summary>Боковые стрелки главного окна: только для фото/пустого экрана.
    /// У видео свои стрелки в оверлее (поверх airspace VLC), поэтому окошные тут прятать —
    /// иначе над видео получаются две стрелки на сторону, и «оконная» не ловит клики.</summary>
    public bool ShowWindowNavArrows =>
        _nav.Items.Count > 1 && CurrentContent is not VideoViewerViewModel && ChromeVisible;

    /// <summary>Инфо-плашка в полноэкранном режиме (имя, размер, порядок файла).
    /// Показывается только когда есть что показывать и видны элементы управления (chrome).</summary>
    public bool ShowFullscreenInfo => IsFullScreen && ChromeVisible && !string.IsNullOrEmpty(StatusText);

    public bool CanToggleClone => _displayTopology.CanToggle;

    public ThumbnailStripPosition ThumbnailStripPosition => _settings.Settings.ThumbnailStripPosition;

    public MainViewModel(
        IMediaLibraryService library,
        INavigationService nav,
        IFileDeletionService deletion,
        ISettingsService settings,
        IDialogService dialog,
        IShellService shell,
        IRecentFilesService recent,
        IThemeService theme,
        IImageCache imageCache,
        IThumbnailService thumbnails,
        LibVlcProvider vlc,
        IPlaybackPositionStore positions,
        INotificationService notify,
        IDisplayTopologyService displayTopology,
        Func<MediaItem, ImageViewerViewModel> imageVmFactory,
        Func<MediaItem, VideoViewerViewModel> videoVmFactory)
    {
        _library = library;
        _nav = nav;
        _deletion = deletion;
        _settings = settings;
        _dialog = dialog;
        _shell = shell;
        _recent = recent;
        _theme = theme;
        _imageCache = imageCache;
        _vlc = vlc;
        _positions = positions;
        _notify = notify;
        _displayTopology = displayTopology;
        _displayTopology.TopologyChanged += (_, _) =>
        {
            CloneDisplayActive = _displayTopology.IsCloned;
            OnPropertyChanged(nameof(CanToggleClone));
            RefreshCommandStates();
        };
        CloneDisplayActive = _displayTopology.IsCloned;

        _imageVmFactory = imageVmFactory;
        _videoVmFactory = videoVmFactory;

        ThumbnailStrip = new ThumbnailStripViewModel(thumbnails);
        ThumbnailStrip.SelectionRequested += (_, item) => _nav.MoveTo(item);

        _nav.CurrentChanged += (_, _) => UpdateCurrentContent();
        _nav.ListChanged += (_, _) => OnListChanged();
        _settings.SettingsChanged += (_, _) => OnSettingsChanged();

        _slideshowTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Clamp(_settings.Settings.SlideshowIntervalSeconds, 1, 60)) };
        _slideshowTimer.Tick += (_, _) => OnSlideshowTick();

        WeakReferenceMessenger.Default.Register<MainViewModel, ToggleFullScreenMessage>(
            this, (r, _) => r.ToggleFullScreen());

        WeakReferenceMessenger.Default.Register<MainViewModel, NavigateFileMessage>(
            this, (r, m) => { if (m.Direction < 0) r._nav.MovePrevious(); else r._nav.MoveNext(); });

        _suppressSortChange = true;
        SelectedSortField = _settings.Settings.SortBy;
        SortDescending = _settings.Settings.SortDescending;
        _suppressSortChange = false;

        CurrentContent = CreateEmptyState();
    }

    /// <summary>Старт приложения: открыть файл из аргументов / последний / пустое состояние.</summary>
    public async Task InitializeAsync(string[] args)
    {
        var path = args.FirstOrDefault(a => File.Exists(a) || Directory.Exists(a));
        if (path != null)
        {
            await OpenPathAsync(path);
            return;
        }

        if (_settings.Settings.OpenLastOnStartup &&
            !string.IsNullOrEmpty(_settings.Settings.LastFilePath) &&
            File.Exists(_settings.Settings.LastFilePath))
        {
            await OpenPathAsync(_settings.Settings.LastFilePath!);
        }
    }

    // --- Реакция на изменения состояния ---

    private async void OnListChanged()
    {
        try
        {
            HasItems = _nav.HasItems;
            await ThumbnailStrip.SetItemsAsync(_nav.Items, _nav.Current);
            ThumbnailStrip.SetCurrent(_nav.Current);
            if (!HasItems && IsSlideshowActive)
            {
                _slideshowTimer.Stop();
                IsSlideshowActive = false;
            }
            OnPropertyChanged(nameof(ShowThumbnailStrip));
            OnPropertyChanged(nameof(ShowNavigation));
            OnPropertyChanged(nameof(ShowWindowNavArrows));
            if (CurrentContent is VideoViewerViewModel video)
                video.ShowFileNavigation = _nav.Items.Count > 1;
            RefreshCommandStates();
        }
        catch (Exception ex)
        {
            AppLog.Error("MainViewModel.OnListChanged", ex);
        }
    }

    private void UpdateCurrentContent()
    {
        var sw = Stopwatch.StartNew();
        var old = CurrentContent;
        var cur = _nav.Current;

        if (old is VideoViewerViewModel reusableVideo && cur is { IsVideo: true })
        {
            reusableVideo.SwitchTo(cur);
            reusableVideo.ShowFileNavigation = _nav.Items.Count > 1;
            ThumbnailStrip.SetCurrent(cur);
            UpdateStatus();
            RefreshCommandStates();
            return;
        }

        object next;
        if (cur == null)
        {
            var empty = CreateEmptyState();
            empty.RefreshRecent();
            next = empty;
        }
        else if (cur.IsImage)
        {
            var vm = _imageVmFactory(cur);
            next = vm;
            _ = vm.LoadAsync();
            PreloadNeighbors();
        }
        else
        {
            var videoVm = _videoVmFactory(cur);
            videoVm.ShowFileNavigation = _nav.Items.Count > 1;
            next = videoVm;
        }

        CurrentContent = next;

        if (old is IDisposable disposable && !ReferenceEquals(old, next))
            Application.Current?.Dispatcher.BeginInvoke(
                new Action(disposable.Dispose), DispatcherPriority.Background);

        ThumbnailStrip.SetCurrent(cur);
        UpdateStatus();
        RefreshCommandStates();
        AppLog.Write($"[Perf] UpdateCurrentContent ({cur?.FileName}): {sw.ElapsedMilliseconds} ms");
    }

    private void PreloadNeighbors()
    {
        var items = _nav.Items;
        if (items.Count < 2) return;
        var idx = _nav.CurrentIndex;
        var next = items[(idx + 1) % items.Count];
        var prev = items[(idx - 1 + items.Count) % items.Count];
        _imageCache.Preload(new[] { next, prev }
            .Where(m => m.MediaType == MediaType.Image)
            .Select(m => m.FullPath));
    }

    private void OnSlideshowTick()
    {
        // Не обрываем недосмотренное видео по интервалу слайда — ждём его окончания (IsEnded),
        // следующий тик после этого переключит на новый файл.
        if (CurrentContent is VideoViewerViewModel { IsEnded: false }) return;
        _nav.MoveNext();
    }

    private void OnSettingsChanged()
    {
        OnPropertyChanged(nameof(ShowThumbnailStrip));
        OnPropertyChanged(nameof(ThumbnailStripPosition));
        if (IsSlideshowActive)
            _slideshowTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(_settings.Settings.SlideshowIntervalSeconds, 1, 60));
    }

    private void UpdateStatus()
    {
        var cur = _nav.Current;
        if (cur == null)
        {
            StatusText = string.Empty;
            CurrentFileName = "Просмотр";
        }
        else
        {
            CurrentFileName = cur.FileName;
            StatusText = $"{FileSizeConverter.Format(cur.FileSizeBytes)} · {cur.FileName} — {_nav.CurrentIndex + 1} из {_nav.Items.Count}";
        }
    }

    private EmptyStateViewModel CreateEmptyState() =>
        new(_recent, OpenFile, OpenFolder, OpenPathAsync);

    private void RefreshCommandStates()
    {
        NextCommand.NotifyCanExecuteChanged();
        PreviousCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        ShowInExplorerCommand.NotifyCanExecuteChanged();
        CopyPathCommand.NotifyCanExecuteChanged();
        OpenWithCommand.NotifyCanExecuteChanged();
        ShowPropertiesCommand.NotifyCanExecuteChanged();
        ToggleSlideshowCommand.NotifyCanExecuteChanged();
        ToggleCloneDisplayCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsFullScreenChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowThumbnailStrip));
        OnPropertyChanged(nameof(ShowFullscreenInfo));
    }
    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(ShowFullscreenInfo));
    partial void OnHasItemsChanged(bool value) => OnPropertyChanged(nameof(ShowThumbnailStrip));
    partial void OnCurrentContentChanged(object? value) => OnPropertyChanged(nameof(ShowWindowNavArrows));
    partial void OnChromeVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowWindowNavArrows));
        OnPropertyChanged(nameof(ShowFullscreenInfo));
    }

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _openCts?.Cancel();
        _openCts?.Dispose();
        _slideshowTimer.Stop();
        ThumbnailStrip.Dispose();
        WeakReferenceMessenger.Default.UnregisterAll(this);

        if (CurrentContent is IDisposable disposable)
        {
            CurrentContent = null;
            disposable.Dispose();
        }
    }
}
