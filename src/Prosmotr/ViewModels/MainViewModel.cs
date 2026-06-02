using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Prosmotr.Infrastructure;
using Prosmotr.Models;
using Prosmotr.Services;
using Prosmotr.Services.Abstractions;

namespace Prosmotr.ViewModels;

/// <summary>Главный оркестратор: открытие, навигация, удаление, полноэкран, слайд-шоу.</summary>
public sealed partial class MainViewModel : ViewModelBase
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
    private readonly IPlaybackPositionStore _positions;
    private readonly INotificationService _notify;

    private readonly DispatcherTimer _slideshowTimer;

    // Последнее удаление в корзину — для отмены (восстановления).
    private MediaItem? _lastDeletedItem;
    private int _lastDeletedIndex;

    public ThumbnailStripViewModel ThumbnailStrip { get; }

    [ObservableProperty] private object? _currentContent;
    [ObservableProperty] private bool _hasItems;
    [ObservableProperty] private bool _isFullScreen;
    [ObservableProperty] private bool _isSlideshowActive;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string _currentFileName = "Просмотр";

    [ObservableProperty] private SortField _selectedSortField = SortField.Name;
    [ObservableProperty] private bool _sortDescending;

    /// <summary>Видны ли «плавающие» элементы поверх контента (нижняя панель фото и боковые стрелки).
    /// Скрываются по таймеру бездействия и снова показываются при движении мыши (управляет MainWindow).</summary>
    [ObservableProperty] private bool _chromeVisible = true;

    private bool _suppressSortChange;
    private string? _currentFolderKey;

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
        INotificationService notify)
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

        ThumbnailStrip = new ThumbnailStripViewModel(thumbnails);
        ThumbnailStrip.SelectionRequested += (_, item) => _nav.MoveTo(item);

        _nav.CurrentChanged += (_, _) => UpdateCurrentContent();
        _nav.ListChanged += (_, _) => OnListChanged();
        _settings.SettingsChanged += (_, _) => OnSettingsChanged();

        _slideshowTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_settings.Settings.SlideshowIntervalSeconds) };
        _slideshowTimer.Tick += (_, _) => _nav.MoveNext();

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

    // --- Открытие ---

    [RelayCommand]
    private async Task OpenFile()
    {
        var path = _dialog.OpenFile(SupportedFormats.AllExtensions);
        if (path != null) await OpenPathAsync(path);
    }

    [RelayCommand]
    private async Task OpenFolder()
    {
        var path = _dialog.OpenFolder();
        if (path != null) await OpenPathAsync(path);
    }

    /// <summary>Открыть путь (файл или папку), построить галерею и перейти к нему.</summary>
    public async Task OpenPathAsync(string path)
    {
        try
        {
            MediaLibraryResult result;
            if (Directory.Exists(path))
            {
                var (sort, order) = await ResolveOrderingAsync(path);
                result = await _library.BuildFromFolderAsync(path, sort, order);
                _recent.Add(path, isFolder: true);
                if (order == null) ReflectSort(sort);
            }
            else if (File.Exists(path) && SupportedFormats.IsSupported(path))
            {
                var folder = Path.GetDirectoryName(path) ?? string.Empty;
                var (sort, order) = await ResolveOrderingAsync(folder);
                result = await _library.BuildFromFileAsync(path, sort, order);
                _recent.Add(path, isFolder: false);
                _settings.Settings.LastFilePath = path;
                _settings.SaveDebounced();
                if (order == null) ReflectSort(sort);
            }
            else
            {
                _notify.Show("Формат файла не поддерживается.", NotificationKind.Warning);
                return;
            }

            if (result.Items.Count == 0)
            {
                _nav.Clear();
                _notify.Show("В папке нет поддерживаемых медиафайлов.", NotificationKind.Warning);
                return;
            }

            ClearUndoState(); // открыли другую галерею — отмена прежнего удаления неактуальна
            _nav.SetItems(result.Items, result.StartIndex);
        }
        catch (Exception ex)
        {
            AppLog.Error("OpenPathAsync", ex);
            _notify.Show("Не удалось открыть файл или папку.", NotificationKind.Error);
        }
    }

    public Task HandleDropAsync(IEnumerable<string> paths)
    {
        var path = paths.FirstOrDefault(p => File.Exists(p) || Directory.Exists(p));
        return path != null ? OpenPathAsync(path) : Task.CompletedTask;
    }

    // --- Навигация ---

    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private void Next() => _nav.MoveNext();

    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private void Previous() => _nav.MovePrevious();

    private bool CanNavigate => _nav.CanGoNext;

    // --- Удаление ---

    private bool _isDeleting;

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task Delete()
    {
        if (_isDeleting) return;
        var cur = _nav.Current;
        if (cur == null) return;

        _isDeleting = true;
        DeleteCommand.NotifyCanExecuteChanged();
        try
        {
            var permanent = _settings.Settings.PermanentDelete;

            if (_settings.Settings.ConfirmDelete)
            {
                var msg = permanent
                    ? $"Удалить «{cur.FileName}» безвозвратно?"
                    : $"Переместить «{cur.FileName}» в Корзину?";
                var confirmed = await _dialog.ConfirmAsync("Удаление файла", msg, "Удалить", "Отмена");
                if (!confirmed) return;
            }

            var index = _nav.CurrentIndex;
            var ok = await _deletion.DeleteAsync(cur.FullPath, permanent);
            if (ok)
            {
                _positions.Remove(cur.FullPath);
                _nav.RemoveAt(index); // удаляем по зафиксированному индексу, а не по текущему —
                                      // иначе во время await пользователь мог сменить файл стрелками

                var notify = _settings.Settings.ShowDeleteNotification;
                if (permanent)
                {
                    ClearUndoState();
                    if (notify)
                        _notify.Show($"«{cur.FileName}» удалён навсегда.", NotificationKind.Success);
                }
                else
                {
                    // Запоминаем для отмены. Восстановить можно кнопкой на панели,
                    // а при включённой плашке — ещё и кнопкой «Отменить» в самом тосте.
                    _lastDeletedItem = cur;
                    _lastDeletedIndex = index;
                    RestoreLastDeleteCommand.NotifyCanExecuteChanged();
                    if (notify)
                        _notify.Show($"«{cur.FileName}» перемещён в корзину.", NotificationKind.Success,
                            "Отменить", () => RestoreLastDeleteCommand.Execute(null));
                }
            }
            else
            {
                _notify.Show("Не удалось удалить файл.", NotificationKind.Error);
            }
        }
        finally
        {
            _isDeleting = false;
            DeleteCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanDelete => !_isDeleting && _nav.Current != null;
    private bool HasCurrent => _nav.Current != null;

    // --- Отмена удаления (восстановление из Корзины) ---

    [RelayCommand(CanExecute = nameof(CanRestore))]
    private async Task RestoreLastDelete()
    {
        var item = _lastDeletedItem;
        if (item == null) return;

        var ok = await RecycleBinRestore.RestoreAsync(item.FullPath);
        if (ok)
        {
            var restored = _library.CreateItem(item.FullPath) ?? item;
            _nav.InsertAt(restored, _lastDeletedIndex);
            ClearUndoState();
            _notify.Show($"«{restored.FileName}» восстановлен.", NotificationKind.Success);
        }
        else
        {
            _notify.Show("Не удалось восстановить файл из корзины.", NotificationKind.Error);
        }
    }

    private bool CanRestore => _lastDeletedItem != null;

    private void ClearUndoState()
    {
        if (_lastDeletedItem == null) return;
        _lastDeletedItem = null;
        RestoreLastDeleteCommand.NotifyCanExecuteChanged();
    }

    // --- Действия с файлом ---

    [RelayCommand(CanExecute = nameof(HasCurrent))]
    private void ShowInExplorer() => Run(p => _shell.ShowInExplorer(p));

    [RelayCommand(CanExecute = nameof(HasCurrent))]
    private void OpenContainingFolder() => Run(p => _shell.OpenContainingFolder(p));

    [RelayCommand(CanExecute = nameof(HasCurrent))]
    private void CopyPath() => Run(p =>
    {
        _shell.CopyPathToClipboard(p);
        _notify.Show("Путь к файлу скопирован.", NotificationKind.Success);
    });

    [RelayCommand(CanExecute = nameof(HasCurrent))]
    private void OpenWith() => Run(p => _shell.OpenWith(p));

    [RelayCommand(CanExecute = nameof(HasCurrent))]
    private void ShowProperties()
    {
        var cur = _nav.Current;
        if (cur != null) PropertiesRequested?.Invoke(cur);
    }

    private void Run(Action<string> action)
    {
        var cur = _nav.Current;
        if (cur != null) action(cur.FullPath);
    }

    // --- Настройки / режимы ---

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke();

    [RelayCommand]
    private void ToggleFullScreen() => IsFullScreen = !IsFullScreen;

    [RelayCommand]
    private void ExitFullScreen() => IsFullScreen = false;

    [RelayCommand(CanExecute = nameof(HasItems))]
    private void ToggleSlideshow()
    {
        if (IsSlideshowActive)
        {
            _slideshowTimer.Stop();
            IsSlideshowActive = false;
        }
        else
        {
            _slideshowTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(_settings.Settings.SlideshowIntervalSeconds, 1, 60));
            _slideshowTimer.Start();
            IsSlideshowActive = true;
        }
    }

    // --- Сортировка ---

    /// <summary>
    /// Определить порядок галереи. Приоритет: ручной выбор пользователя для папки →
    /// реальный порядок открытого окна Проводника → глобальная настройка.
    /// Возвращает либо поле сортировки (sort), либо готовый порядок путей (order).
    /// </summary>
    private async Task<(SortSpec sort, IReadOnlyList<string>? order)> ResolveOrderingAsync(string folder)
    {
        var key = string.IsNullOrEmpty(folder) ? null : folder.ToLowerInvariant().TrimEnd('\\', '/');
        _currentFolderKey = key;

        // 1) Явный выбор пользователя для этой папки — высший приоритет.
        if (key != null && _settings.Settings.ManualFolderSorts.TryGetValue(key, out var manual) &&
            TryParseSpec(manual, out var manualSpec))
        {
            AppLog.Write($"Сортировка (выбор пользователя): {manualSpec.Field}, убыв={manualSpec.Descending}");
            return (manualSpec, null);
        }

        // 2) Реальный порядок открытого окна Проводника — повторяет ЛЮБУЮ сортировку Windows.
        if (_settings.Settings.MatchExplorerSort && !string.IsNullOrEmpty(folder))
        {
            var paths = await Task.Run(() =>
            {
                if (ExplorerSortReader.TryGetOrderedPaths(folder, out var p) && p.Count > 0)
                    return p;
                return null;
            });
            if (paths != null)
            {
                AppLog.Write($"Порядок из Проводника: {paths.Count} файлов");
                return (default, paths);
            }
        }

        // 3) Глобальная настройка по умолчанию.
        AppLog.Write($"Сортировка из настроек: {_settings.Settings.SortBy}, убыв={_settings.Settings.SortDescending}");
        return (new SortSpec(_settings.Settings.SortBy, _settings.Settings.SortDescending), null);
    }

    private void ReflectSort(SortSpec sort)
    {
        _suppressSortChange = true;
        SelectedSortField = sort.Field;
        SortDescending = sort.Descending;
        _suppressSortChange = false;
    }

    partial void OnSelectedSortFieldChanged(SortField value)
    {
        if (!_suppressSortChange) PersistAndApplySort();
    }

    partial void OnSortDescendingChanged(bool value)
    {
        if (!_suppressSortChange) PersistAndApplySort();
    }

    [RelayCommand]
    private void ToggleSortDirection() => SortDescending = !SortDescending;

    private void PersistAndApplySort()
    {
        _settings.Settings.SortBy = SelectedSortField;
        _settings.Settings.SortDescending = SortDescending;
        // Запоминаем выбор пользователя для текущей папки — он перекрывает Проводник при след. открытии.
        if (_currentFolderKey != null)
            _settings.Settings.ManualFolderSorts[_currentFolderKey] = $"{SelectedSortField}:{SortDescending}";
        _settings.SaveDebounced();
        ApplySort();
    }

    private static bool TryParseSpec(string value, out SortSpec spec)
    {
        spec = default;
        var parts = value.Split(':');
        if (parts.Length == 2 && Enum.TryParse<SortField>(parts[0], out var f) && bool.TryParse(parts[1], out var d))
        {
            spec = new SortSpec(f, d);
            return true;
        }
        return false;
    }

    private void ApplySort()
    {
        if (!_nav.HasItems) return;
        var sorted = _library.Sort(_nav.Items, new SortSpec(SelectedSortField, SortDescending));
        _nav.ReorderPreservingCurrent(sorted);
    }

    // --- Реакция на изменения состояния ---

    private async void OnListChanged()
    {
        HasItems = _nav.HasItems;
        await ThumbnailStrip.SetItemsAsync(_nav.Items);
        ThumbnailStrip.SetCurrent(_nav.Current); // сохранить подсветку (в т.ч. после пересортировки)
        if (!HasItems && IsSlideshowActive)
        {
            _slideshowTimer.Stop();
            IsSlideshowActive = false;
        }
        OnPropertyChanged(nameof(ShowThumbnailStrip));
        OnPropertyChanged(nameof(ShowNavigation));
        OnPropertyChanged(nameof(ShowWindowNavArrows));
        // Обновить видимость боковых кнопок перехода у текущего видео (список мог измениться).
        if (CurrentContent is VideoViewerViewModel video)
            video.ShowFileNavigation = _nav.Items.Count > 1;
        RefreshCommandStates();
    }

    private void UpdateCurrentContent()
    {
        var old = CurrentContent;
        var cur = _nav.Current;

        // Видео→видео: переиспользуем тот же плеер и окно — плавно, без рывков и нового окна.
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
            var vm = new ImageViewerViewModel(cur, _imageCache, _dialog, _notify);
            next = vm;
            _ = vm.LoadAsync();
            PreloadNeighbors();
        }
        else // видео
        {
            var videoVm = new VideoViewerViewModel(cur, _vlc, _settings, _positions, _dialog, _notify);
            videoVm.ShowFileNavigation = _nav.Items.Count > 1;
            next = videoVm;
        }

        CurrentContent = next;

        // Освобождаем предыдущий контент (видео-плеер) после визуальной замены.
        // Тип контента сменился (видео↔фото/пусто) → ContentControl сам пересоздаёт View,
        // старый VideoViewerView выгружается и закрывает своё наложенное окно.
        if (old is IDisposable disposable && !ReferenceEquals(old, next))
            Application.Current?.Dispatcher.BeginInvoke(
                new Action(disposable.Dispose), DispatcherPriority.Background);

        ThumbnailStrip.SetCurrent(cur);
        UpdateStatus();
        RefreshCommandStates();
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
            StatusText = $"{cur.FileSizeText} · {cur.FileName} — {_nav.CurrentIndex + 1} из {_nav.Items.Count}";
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
        OpenContainingFolderCommand.NotifyCanExecuteChanged();
        CopyPathCommand.NotifyCanExecuteChanged();
        OpenWithCommand.NotifyCanExecuteChanged();
        ShowPropertiesCommand.NotifyCanExecuteChanged();
        ToggleSlideshowCommand.NotifyCanExecuteChanged();
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
}
