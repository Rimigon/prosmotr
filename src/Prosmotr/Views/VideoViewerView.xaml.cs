using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Prosmotr.ViewModels;

namespace Prosmotr.Views;

public partial class VideoViewerView : UserControl
{
    private readonly DispatcherTimer _hideTimer;
    private readonly DispatcherTimer _clickTimer;
    private readonly DispatcherTimer _pauseShowTimer;
    private VideoViewerViewModel? _vm;
    private bool _suppressSlider;
    private bool _controlsShown = true;
    private MainViewModel? _mainVm;
    private ContextMenu? _audioMenu;
    private ContextMenu? _subtitleMenu;
    private ContextMenu? _speedMenu;
    private long _audioMenuClosedAt;
    private long _subtitleMenuClosedAt;
    private long _speedMenuClosedAt;
    private const long MenuToggleThresholdMs = 350;

    public VideoViewerView()
    {
        InitializeComponent();

        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _hideTimer.Tick += OnHideTimerTick;

        _clickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        _clickTimer.Tick += OnSingleClickElapsed;

        // Показ панели на паузе — с задержкой, чтобы при быстром переходе видео→видео
        // (кратковременная остановка плеера) панель не мелькала.
        _pauseShowTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
        _pauseShowTimer.Tick += OnPauseShowTimerTick;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;

        Overlay.MouseMove += (_, _) => ShowControls();
        ClickArea.MouseLeftButtonDown += OnClickAreaDown;
        PositionSlider.ValueChanged += OnSliderValueChanged;

        // Контекстное меню по правому клику (аудио, субтитры, скорость, действия с файлом).
        Overlay.ContextMenu = new ContextMenu();
        Overlay.ContextMenuOpening += OnContextMenuOpening;

        WeakReferenceMessenger.Default.Register<VideoViewerView, ToggleChromeMessage>(this, static (r, _) => r.OnToggleChrome());
    }

    private MainViewModel? MainVm => Window.GetWindow(this)?.DataContext as MainViewModel;

    // ContentControl переиспользует этот View при переходе видео→видео (тот же тип VM):
    // привязываем новый плеер и запускаем здесь, иначе остаётся старый (пустой кадр).
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Detach();
        if (DataContext is VideoViewerViewModel vm)
        {
            _vm = vm;
            vm.PropertyChanged += OnVmPropertyChanged;
            Video.MediaPlayer = vm.Player;
            if (IsLoaded)
            {
                ShowControls();
                vm.Start();
                FocusHostWindow();
            }
            // если ещё не загружены — стартуем в OnLoaded (нативный HWND будет готов)
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        Video.MediaPlayer = _vm.Player;
        ShowControls();
        _vm.Start();
        Dispatcher.BeginInvoke(new Action(FocusHostWindow), DispatcherPriority.Loaded);
        _mainVm = Window.GetWindow(this)?.DataContext as MainViewModel;
        if (_mainVm != null) _mainVm.PropertyChanged += OnMainVmPropertyChanged;
        UpdateInfo();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _hideTimer.Stop();
        _hideTimer.Tick -= OnHideTimerTick;
        _clickTimer.Stop();
        _clickTimer.Tick -= OnSingleClickElapsed;
        _pauseShowTimer.Stop();
        _pauseShowTimer.Tick -= OnPauseShowTimerTick;

        // Закрываем и освобождаем меню, чтобы не держать делегаты и COM-ссылки.
        _speedMenu?.SetValue(ContextMenu.IsOpenProperty, false);
        _audioMenu?.SetValue(ContextMenu.IsOpenProperty, false);
        _subtitleMenu?.SetValue(ContextMenu.IsOpenProperty, false);
        _speedMenu = null;
        _audioMenu = null;
        _subtitleMenu = null;

        WeakReferenceMessenger.Default.Unregister<ToggleChromeMessage>(this);
        DetachMainVm();
        Detach();
    }

    private void Detach()
    {
        if (_vm != null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm = null;
        }
        try { Video.MediaPlayer = null; } catch { /* уже освобождён */ }
    }

    private void FocusHostWindow()
    {
        var w = Window.GetWindow(this);
        if (w == null) return;
        w.Activate();
        Keyboard.Focus(w);
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_vm == null) return;

        if (e.PropertyName == nameof(VideoViewerViewModel.PositionMs))
        {
            _suppressSlider = true;
            PositionSlider.Value = _vm.PositionMs;
            _suppressSlider = false;
        }
        else if (e.PropertyName == nameof(VideoViewerViewModel.IsPlaying))
        {
            if (!_vm.IsPlaying)
                _pauseShowTimer.Start();   // покажем панель, если остановка «настоящая» (пауза)
            else
            {
                _pauseShowTimer.Stop();    // снова играет (был быстрый переход) — не мигаем
                // Видео реально стартовало — нативное окно VLC могло перехватить фокус.
                // Возвращаем клавиатурный фокус окну, чтобы горячие клавиши работали без клика.
                Dispatcher.BeginInvoke(new Action(FocusHostWindow), DispatcherPriority.Background);
            }
        }
        else if (e.PropertyName == nameof(VideoViewerViewModel.ShowFileNavigation))
        {
            UpdateSideNav();
        }
    }

    private void OnSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressSlider || _vm == null) return;
        _vm.SeekTo(e.NewValue);
    }

    // --- Клик по области видео: один — пауза, двойной — полный экран ---

    private void OnClickAreaDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        _clickTimer.Stop();
        if (e.ClickCount == 2)
            ToggleFullScreen();
        else
            _clickTimer.Start();

        FocusHostWindow(); // вернуть клавиатурный фокус окну после клика по видео
    }

    private void OnSingleClickElapsed(object? sender, EventArgs e)
    {
        _clickTimer.Stop();
        _vm?.TogglePlayCommand.Execute(null);
    }

    private void OnHideTimerTick(object? sender, EventArgs e) => HideControlsIfPlaying();

    private void OnPauseShowTimerTick(object? sender, EventArgs e)
    {
        _pauseShowTimer.Stop();
        if (_vm is { IsPlaying: false, IsEnded: false }) ShowControls();
    }

    private void OnSpeedButtonClick(object sender, RoutedEventArgs e)
    {
        if (_speedMenu?.IsOpen == true)
        {
            _speedMenu.IsOpen = false;
            return;
        }
        if (Environment.TickCount64 - _speedMenuClosedAt < MenuToggleThresholdMs)
            return;
        if (_vm == null) return;

        _speedMenu = new ContextMenu
        {
            PlacementTarget = SpeedButton,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Top
        };
        AddSpeedItems(_speedMenu.Items);

        _speedMenu.Closed += (_, _) => _speedMenuClosedAt = Environment.TickCount64;
        _speedMenu.IsOpen = true;
    }

    private void OnAudioButtonClick(object sender, RoutedEventArgs e)
    {
        if (_audioMenu?.IsOpen == true)
        {
            _audioMenu.IsOpen = false;
            return;
        }
        if (Environment.TickCount64 - _audioMenuClosedAt < MenuToggleThresholdMs)
            return;
        if (_vm == null) return;

        _audioMenu = new ContextMenu
        {
            PlacementTarget = AudioButton,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Top
        };
        AddAudioItems(_audioMenu.Items);

        _audioMenu.Closed += (_, _) => _audioMenuClosedAt = Environment.TickCount64;
        _audioMenu.IsOpen = true;
    }

    private void OnSubtitleButtonClick(object sender, RoutedEventArgs e)
    {
        if (_subtitleMenu?.IsOpen == true)
        {
            _subtitleMenu.IsOpen = false;
            return;
        }
        if (Environment.TickCount64 - _subtitleMenuClosedAt < MenuToggleThresholdMs)
            return;
        if (_vm == null) return;

        _subtitleMenu = new ContextMenu
        {
            PlacementTarget = SubtitleButton,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Top
        };
        AddSubtitleItems(_subtitleMenu.Items);

        _subtitleMenu.Closed += (_, _) => _subtitleMenuClosedAt = Environment.TickCount64;
        _subtitleMenu.IsOpen = true;
    }

    // --- Построители пунктов (используются и кнопками панели, и контекстным меню) ---

    private void AddSpeedItems(ItemCollection items)
    {
        if (_vm == null) return;
        foreach (var option in _vm.AvailableRates)
        {
            var value = option.Value;
            items.Add(MediaContextMenu.Check(option.Label, Math.Abs(_vm.Rate - value) < 0.001f,
                () => _vm?.SetRate(value)));
        }
    }

    private void AddAudioItems(ItemCollection items)
    {
        if (_vm == null) return;
        var tracks = _vm.GetAudioTracks();
        if (tracks.Count == 0)
        {
            items.Add(new MenuItem { Header = "Нет аудиодорожек", IsEnabled = false });
            return;
        }
        foreach (var track in tracks)
        {
            var id = track.Id;
            items.Add(MediaContextMenu.Check(track.Name, track.IsCurrent, () => _vm?.SelectAudioTrack(id)));
        }
    }

    private void AddSubtitleItems(ItemCollection items)
    {
        if (_vm == null) return;
        foreach (var track in _vm.GetSubtitleTracks())
        {
            var id = track.Id;
            items.Add(MediaContextMenu.Check(track.Name, track.IsCurrent, () => _vm?.SelectSubtitle(id)));
        }
        items.Add(new Separator());
        items.Add(MediaContextMenu.Item("Загрузить файл субтитров…",
            () => _vm?.LoadSubtitleCommand.Execute(null), icon: Wpf.Ui.Controls.SymbolRegular.DocumentArrowDown24));
    }

    // --- Контекстное меню видео (правый клик) ---

    private void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (_vm == null || Overlay.ContextMenu is not ContextMenu menu) { e.Handled = true; return; }
        var items = menu.Items;
        items.Clear();
        var main = MainVm;

        items.Add(MediaContextMenu.Item(_vm.IsPlaying ? "Пауза" : "Воспроизведение",
            () => _vm?.TogglePlayCommand.Execute(null),
            icon: _vm.IsPlaying ? Wpf.Ui.Controls.SymbolRegular.Pause24 : Wpf.Ui.Controls.SymbolRegular.Play24));

        if (main != null) MediaContextMenu.AddNavigation(items, main);

        items.Add(new Separator());

        var audio = new MenuItem { Header = "Аудиодорожка", Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.MusicNote124 } };
        AddAudioItems(audio.Items);
        items.Add(audio);

        var subs = new MenuItem { Header = "Субтитры", Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.ClosedCaption24 } };
        AddSubtitleItems(subs.Items);
        items.Add(subs);

        var speed = new MenuItem { Header = "Скорость", Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.TopSpeed24 } };
        AddSpeedItems(speed.Items);
        items.Add(speed);

        items.Add(MediaContextMenu.Item("Сохранить кадр", () => _vm?.TakeSnapshotCommand.Execute(null),
            icon: Wpf.Ui.Controls.SymbolRegular.Camera24));

        items.Add(new Separator());
        items.Add(MediaContextMenu.Item("Полный экран", ToggleFullScreen,
            icon: Wpf.Ui.Controls.SymbolRegular.FullScreenMaximize24));

        if (main != null)
        {
            items.Add(new Separator());
            MediaContextMenu.AddFileActions(items, main);
        }
    }

    private void OnPrevFileClick(object sender, RoutedEventArgs e) =>
        WeakReferenceMessenger.Default.Send(new NavigateFileMessage(-1));

    private void OnNextFileClick(object sender, RoutedEventArgs e) =>
        WeakReferenceMessenger.Default.Send(new NavigateFileMessage(1));

    private void OnFullScreenClick(object sender, RoutedEventArgs e) => ToggleFullScreen();

    private static void ToggleFullScreen() =>
        WeakReferenceMessenger.Default.Send(new ToggleFullScreenMessage());

    // --- Автоскрытие панели и курсора ---

    private void ShowControls()
    {
        ControlBar.Visibility = Visibility.Visible;
        // Устанавливаем курсор на Overlay (а не на UserControl), чтобы он точно применялся
        // над областью видео внутри airspace LibVLCSharp.WPF.
        Overlay.Cursor = Cursors.Arrow;
        // Также возвращаем курсор окна, если видео его скрыло — чтобы панели/стрелки снаружи тоже были с курсором.
        if (Window.GetWindow(this) is { } w) w.Cursor = Cursors.Arrow;
        _controlsShown = true;
        UpdateSideNav();
        UpdateInfo();
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void HideControlsIfPlaying()
    {
        _hideTimer.Stop();
        if (_vm is { IsPlaying: true, IsEnded: false })
        {
            ControlBar.Visibility = Visibility.Collapsed;
            // Скрываем курсор над видео: явно на Overlay (чтобы airspace не мешал)
            // и на окно (чтобы не оставался Arrow от MainWindow/иных элементов).
            Overlay.Cursor = Cursors.None;
            if (Window.GetWindow(this) is { } w) w.Cursor = Cursors.None;
            _controlsShown = false;
            UpdateSideNav();
            UpdateInfo();
        }
    }

    // Боковые стрелки перехода между файлами показываются только вместе с панелью управления
    // (и только когда файлов больше одного) — прячутся по таймеру одновременно с ней.
    private void UpdateSideNav()
    {
        var show = _controlsShown && _vm?.ShowFileNavigation == true;
        var visibility = show ? Visibility.Visible : Visibility.Collapsed;
        PrevFileButton.Visibility = visibility;
        NextFileButton.Visibility = visibility;
    }

    private void OnToggleChrome()
    {
        if (_controlsShown)
        {
            ControlBar.Visibility = Visibility.Collapsed;
            Overlay.Cursor = Cursors.None;
            if (Window.GetWindow(this) is { } w) w.Cursor = Cursors.None;
            _controlsShown = false;
            UpdateSideNav();
            UpdateInfo();
            _hideTimer.Stop();
        }
        else
        {
            ShowControls();
        }
    }

    private void DetachMainVm()
    {
        if (_mainVm != null)
        {
            _mainVm.PropertyChanged -= OnMainVmPropertyChanged;
            _mainVm = null;
        }
    }

    private void OnMainVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsFullScreen)
            || e.PropertyName == nameof(MainViewModel.StatusText)
            || e.PropertyName == nameof(MainViewModel.ShowFullscreenInfo))
        {
            UpdateInfo();
        }
    }

    private void UpdateInfo()
    {
        if (FullscreenInfoBorder == null || InfoText == null) return;
        bool show = _mainVm?.IsFullScreen == true && _controlsShown && !string.IsNullOrEmpty(_mainVm.StatusText);
        InfoText.Text = _mainVm?.StatusText ?? string.Empty;
        FullscreenInfoBorder.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }
}
