using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Prosmotr.Infrastructure;
using Prosmotr.ViewModels;

namespace Prosmotr.Views;

public partial class VideoViewerView : UserControl
{
    private readonly DispatcherTimer _hideTimer;
    private readonly DispatcherTimer _clickTimer;
    private readonly DispatcherTimer _pauseShowTimer;
    private readonly DispatcherTimer _seekThrottle;
    private readonly DispatcherTimer _seekCooldown;
    private VideoViewerViewModel? _vm;
    private bool _suppressSlider;
    private bool _isSeekDragging;
    private double _pendingSeekMs;
    private bool _hasPendingSeek;
    private bool _controlsShown = true;
    private DateTime _suppressMouseMoveUntil = DateTime.MinValue;
    private Point _lastMousePosition;
    private MainViewModel? _mainVm;
    private ContextMenu? _audioMenu;
    private ContextMenu? _subtitleMenu;
    private ContextMenu? _speedMenu;
    private ProgressBar? _miniTimeline;
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

        // Дросселирование перемоток при перетаскивании ползунка: без него каждое
        // микродвижение слайдера слало бы отдельный seek в VLC, и декодер захлёбывался
        // (визуальные лаги/артефакты). Так перемотка идёт не чаще ~раз в 120 мс.
        _seekThrottle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _seekThrottle.Tick += OnSeekThrottleTick;

        // После drag seek'а плеер некоторое время присылает TimeChanged со старыми позициями,
        // пока декодер не приземлится. Без задержки сброса _suppressSlider ползунок резко
        // дергается назад, а следующее движение пользователя — вперёд, создавая скачки.
        _seekCooldown = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _seekCooldown.Tick += OnSeekCooldownTick;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;

        Overlay.MouseMove += OnOverlayMouseMove;
        ClickArea.MouseLeftButtonDown += OnClickAreaDown;
        PositionSlider.ValueChanged += OnSliderValueChanged;
        // Начало/конец перетаскивания «бегунка» слайдера (routed-события Thumb всплывают к Slider).
        PositionSlider.AddHandler(
            System.Windows.Controls.Primitives.Thumb.DragStartedEvent,
            new System.Windows.Controls.Primitives.DragStartedEventHandler(OnSeekDragStarted));
        PositionSlider.AddHandler(
            System.Windows.Controls.Primitives.Thumb.DragCompletedEvent,
            new System.Windows.Controls.Primitives.DragCompletedEventHandler(OnSeekDragCompleted));

        // Контекстное меню по правому клику (аудио, субтитры, скорость, действия с файлом).
        Overlay.ContextMenu = new ContextMenu();
        Overlay.ContextMenuOpening += OnContextMenuOpening;

        WeakReferenceMessenger.Default.Register<VideoViewerView, ToggleChromeMessage>(this, static (r, _) => r.OnToggleChrome());
    }

    private MainViewModel? MainVm => ResolveMainVm();

    /// <summary>
    /// Найти MainViewModel, к которому привязано это View. Window.GetWindow(this) может вернуть
    /// ForegroundWindow LibVLCSharp.WPF (у него нет DataContext), поэтому есть fallback на
    /// Application.Current.MainWindow — в этом приложении оно всегда главное окно.
    /// </summary>
    private MainViewModel? ResolveMainVm()
    {
        if (Window.GetWindow(this)?.DataContext is MainViewModel mainVm)
            return mainVm;
        if (Application.Current?.MainWindow?.DataContext is MainViewModel fallbackVm)
            return fallbackVm;
        return null;
    }

    // ContentControl переиспользует этот View при переходе видео→видео (тот же тип VM):
    // привязываем новый плеер и запускаем здесь, иначе остаётся старый (пустой кадр).
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Detach();
        if (DataContext is VideoViewerViewModel vm)
        {
            _vm = vm;
            vm.PropertyChanged += OnVmPropertyChanged;
            // Перепривязываем _mainVm ДО запуска плеера, чтобы восстановление видимости
            // панели могло опираться на актуальное ChromeVisible главного окна.
            AttachMainVm(ResolveMainVm());
            UpdateCover(); // сначала поднимаем чёрный cover
            if (IsLoaded)
            {
                // Даём WPF один полный render-цикл, чтобы cover отрисовался поверх
                // нативного HWND LibVLC ДО смены Media/Play — иначе белый фон HWND
                // успевает мелькнуть раньше cover.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    Video.MediaPlayer = vm.Player;
                    RestoreControls(); // сохраняем состояние элементов управления при переходе
                    vm.Start();
                    FocusHostWindow();
                }), DispatcherPriority.Render);
            }
            else
            {
                // Новый View ещё не в дереве — ResolveMainVm вернёт null.
                // Состояние панели восстановим в OnLoaded, когда окно уже доступно.
                // noop: mainVm будет доступен в OnLoaded.
            }
            UpdateCloneButton();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        // При восстановлении из PiP плеер уже инициализирован и дорожка загружена;
        // не вызываем Start(), чтобы не начать воспроизведение заново.
        if (_vm.IsPictureInPicture)
        {
            AttachMainVm(ResolveMainVm());
            _miniTimeline = MiniTimeline;
            Video.MediaPlayer = _vm.Player;
            IsVisibleChanged -= OnVisibilityRestored;
            IsVisibleChanged += OnVisibilityRestored;
            UpdateCover();
            UpdateCloneButton();
            return;
        }

        // Идемпотентная привязка: при reuse-сценарии OnDataContextChanged мог уже подписать
        // тот же _mainVm — без этого было два += против одного -= (утечка View через singleton).
        AttachMainVm(ResolveMainVm());
        _miniTimeline = MiniTimeline; // FindName доступен после InitializeComponent
        UpdateCover(); // cover вверх до старта, чтобы не мелькал белый фон нативного HWND
        // Отрисовываем cover перед сменой Media/Play: сначала Render, потом уже плеер.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            Video.MediaPlayer = _vm.Player;
            RestoreControls(); // не показываем панель, если она была скрыта до переключения
            _vm.Start();
        }), DispatcherPriority.Render);
        Dispatcher.BeginInvoke(new Action(FocusHostWindow), DispatcherPriority.Loaded);
        UpdateCloneButton();
    }

    private void OnVisibilityRestored(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && _vm is { IsPictureInPicture: true })
        {
            // Когда плейсхолдер заменяется на VideoViewerView, VideoView получает HWND
            // и должен перехватить вывод плеера. Пока View была невидима, кадр мог
            // остаться в PiP-окне; принудительно перезапускаем видеовывод.
            Video.MediaPlayer = _vm.Player;
            _vm.PlaybackService.Play();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _hideTimer.Stop();
        _hideTimer.Tick -= OnHideTimerTick;
        _clickTimer.Stop();
        _clickTimer.Tick -= OnSingleClickElapsed;
        _pauseShowTimer.Stop();
        _pauseShowTimer.Tick -= OnPauseShowTimerTick;
        _seekThrottle.Stop();
        _seekThrottle.Tick -= OnSeekThrottleTick;
        _seekCooldown.Stop();
        _seekCooldown.Tick -= OnSeekCooldownTick;
        _isSeekDragging = false;
        _hasPendingSeek = false;

        // Закрываем и освобождаем меню, чтобы не держать делегаты и COM-ссылки.
        _speedMenu?.SetValue(ContextMenu.IsOpenProperty, false);
        _audioMenu?.SetValue(ContextMenu.IsOpenProperty, false);
        _subtitleMenu?.SetValue(ContextMenu.IsOpenProperty, false);
        _speedMenu = null;
        _audioMenu = null;
        _subtitleMenu = null;
        _miniTimeline = null;

        WeakReferenceMessenger.Default.Unregister<ToggleChromeMessage>(this);
        DetachMainVm();
        // При удалении View из дерева обычно останавливаем плеер и освобождаем Media,
        // чтобы нативное окно LibVLC не висело поверх WPF. ИСКЛЮЧЕНИЕ: вход в режим
        // Picture-in-Picture — плеер временно переносится в отдельное плавающее окно,
        // поэтому останавливать/освобождать его нельзя. При этом сбрасываем MediaPlayer
        // у основного VideoView, чтобы старый HWND не перехватывал вывод.
        if (_vm is { IsPictureInPicture: false })
            _vm?.StopAndRelease();
        else
            try { Video.MediaPlayer = null; } catch { }

        Detach();
    }

    private void Detach()
    {
        if (_vm != null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm = null;
        }
        // Если идём в PiP, плеер используется в другом окне — не трогаем привязку здесь,
        // чтобы не потерять MediaPlayer; иначе очищаем VideoView.
        if (_vm is not { IsPictureInPicture: true })
            try { Video.MediaPlayer = null; } catch { }
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
            // Во время перетаскивания и некоторое время после seek'а не двигаем ползунок
            // под курсором пользователя — позицией управляет drag, а события плеера
            // подхватятся после "приземления" декодера (см. _seekCooldown).
            if (_isSeekDragging || _seekCooldown.IsEnabled) return;
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
        else if (e.PropertyName == nameof(VideoViewerViewModel.IsBuffering)
                 || e.PropertyName == nameof(VideoViewerViewModel.CanShowMiniTimeline)
                 || e.PropertyName == nameof(VideoViewerViewModel.IsEnded)
                 || e.PropertyName == nameof(VideoViewerViewModel.LengthMs))
        {
            UpdateChromeVisibility();
            UpdateCover();
        }
    }

    /// <summary>
    /// Синхронизировать чёрный cover со статусом «загрузка первого кадра».
    /// Cover перекрывает нативный HWND LibVLC (через оверлей ForegroundWindow), скрывая
    /// его светлый фон (белый квадрат) до готовности первого кадра. Пока cover активен,
    /// также скрываем панель управления/стрелки/инфо, чтобы белый фон не просвечивал через
    /// их полупрозрачные области.
    /// </summary>
    private void UpdateCover()
    {
        if (SwitchCover == null) return;
        var visible = _vm?.IsBuffering == true;
        AppLog.Write($"[Flicker] UpdateCover IsBuffering={_vm?.IsBuffering} Setting={visible}");
        SwitchCover.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        UpdateChromeVisibility();
    }

    private void OnSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressSlider || _vm == null) return;
        if (_isSeekDragging)
        {
            // Перетаскивание: копим целевую позицию и перематываем дросселированно
            // (см. OnSeekThrottleTick), чтобы не заваливать декодер VLC потоком seek'ов.
            _pendingSeekMs = e.NewValue;
            _hasPendingSeek = true;
            if (!_seekThrottle.IsEnabled) _seekThrottle.Start();
        }
        else
        {
            // Клик по дорожке (IsMoveToPointEnabled) — одиночная перемотка.
            _vm.SeekTo(e.NewValue);
        }
    }

    private void OnSeekDragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
        => _isSeekDragging = true;

    private void OnSeekDragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        _isSeekDragging = false;
        _seekThrottle.Stop();
        _hasPendingSeek = false;
        // Финальная перемотка на отпущенную позицию. Для drag не обновляем PositionMs
        // из VM — ползунок уже стоит у пользователя; актуальная позиция придёт позже.
        _vm?.SeekTo(PositionSlider.Value, isDrag: true);
        ArmSeekCooldown();
    }

    private void OnSeekThrottleTick(object? sender, EventArgs e)
    {
        _seekThrottle.Stop();
        if (_hasPendingSeek && _vm != null)
        {
            _hasPendingSeek = false;
            // Drag таймлайна уже дросселирован 120 мс; здесь не обновляем PositionMs
            // принудительно, чтобы ползунок не прыгал на keyframe fast-seek'а.
            _vm.SeekTo(_pendingSeekMs, isDrag: true);
            ArmSeekCooldown();
        }
    }

    private void ArmSeekCooldown()
    {
        // После drag seek'а игнорируем TimeChanged от плеера ~180 мс: декодер ещё
        // "приземляется" и может прислать промежуточную позицию, из-за чего ползунок
        // дергается назад/вперёд. По окончании cooldown'а ползунок синхронизируется
        // через обычный OnVmPropertyChanged (PositionMs).
        _seekCooldown.Stop();
        _seekCooldown.Start();
    }

    private void OnSeekCooldownTick(object? sender, EventArgs e)
    {
        _seekCooldown.Stop();
        // Cooldown только снимает блокировку с TimeChanged; принудительная синхронизация
        // здесь не нужна — следующее актуальное событие от плеера обновит ползунок.
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
        // Во время буферизации не возвращаем панель — иначе cover перестанет перекрывать
        // нижнюю часть и белый фон нативного HWND просветит через полупрозрачную панель.
        if (_vm is { IsPlaying: false, IsEnded: false, IsBuffering: false })
        {
            ShowControls();
        }
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

        items.Add(MediaContextMenu.Item("Окно в окне", () => _mainVm?.TogglePictureInPictureCommand.Execute(null),
            icon: Wpf.Ui.Controls.SymbolRegular.WindowAd20));
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
    private void OnPictureInPictureClick(object sender, RoutedEventArgs e)
    {
        AppLog.Write($"VideoViewerView.OnPictureInPictureClick: _mainVm={_mainVm != null}");
        _mainVm?.TogglePictureInPictureCommand.Execute(null);
    }
    private void OnCloneDisplayClick(object sender, RoutedEventArgs e)
    {
        AppLog.Write($"VideoViewerView.OnCloneDisplayClick: _mainVm={_mainVm != null}, CanToggleClone={_mainVm?.CanToggleClone}");
        _mainVm?.ToggleCloneDisplayCommand.Execute(null);
    }

    private static void ToggleFullScreen() =>
        WeakReferenceMessenger.Default.Send(new ToggleFullScreenMessage());

    private void OnOverlayMouseMove(object sender, MouseEventArgs e)
    {
        // При переключении фото→видео (или при удалении видео) Overlay сразу получает
        // MouseMove от текущего положения курсора. Если панель была скрыта, это
        // не должно её показывать — пользователь ещё не двинул мышью.
        // Игнорируем движение, пока не истекло окно подавления и курсор не сдвинулся
        // значительно от точки восстановления.
        if (DateTime.Now < _suppressMouseMoveUntil)
            return;

        var pos = e.GetPosition(Overlay);
        var delta = new Vector(pos.X - _lastMousePosition.X, pos.Y - _lastMousePosition.Y);
        if (delta.Length < 4)
            return;

        _lastMousePosition = pos;
        ShowControls();
    }

    // --- Автоскрытие панели и курсора ---

    private void ShowControls()
    {
        _controlsShown = true;
        UpdateChromeVisibility();
        SyncChromeVisible();
        _hideTimer.Stop();
        // Если автоскрытие отключено настройкой — таймер не запускаем, панель остаётся видимой.
        if (_vm == null || _vm.AutoHideControls)
            _hideTimer.Start();
    }

    private void RestoreControls()
    {
        // При переключении между фото и видео панель должна оставаться в том же состоянии,
        // в котором была раньше: если была скрыта — не появляться, если была видна — остаться видимой.
        // Страховка: если _mainVm не привязался (airspace-окно LibVLC вместо MainWindow),
        // повторно пытаемся найти MainViewModel прямо перед восстановлением.
        if (_mainVm == null)
            AttachMainVm(ResolveMainVm());

        var chrome = _mainVm?.ChromeVisible ?? true;
        _controlsShown = chrome;
        // Запоминаем точку курсора и окно подавления, чтобы первый MouseMove
        // после восстановления не вызвал ShowControls(), если мышь не двигалась.
        _lastMousePosition = Mouse.GetPosition(Overlay);
        _suppressMouseMoveUntil = DateTime.Now.AddMilliseconds(120);
        UpdateChromeVisibility();
        _hideTimer.Stop();
        if (_controlsShown && _vm?.AutoHideControls == true)
            _hideTimer.Start();
    }

    private void HideControlsIfPlaying()
    {
        _hideTimer.Stop();
        if (_vm is { IsPlaying: true, IsEnded: false, IsBuffering: false })
        {
            _controlsShown = false;
            UpdateChromeVisibility();
            SyncChromeVisible();
        }
    }

    /// <summary>
    /// Поддерживает единое состояние видимости плавающих элементов между фото и видео:
    /// VideoViewerView управляет своей панелью, но синхронизирует его с MainViewModel.ChromeVisible,
    /// чтобы при переходе на фото панель/стрелки/курсор оставались в том же состоянии.
    /// </summary>
    private void SyncChromeVisible()
    {
        if (_mainVm == null) return;
        if (_mainVm.ChromeVisible != _controlsShown)
            _mainVm.ChromeVisible = _controlsShown;
    }

    /// <summary>
    /// Синхронизировать видимость панели/стрелок/инфо-плашки с учётом буферизации.
    /// Пока идёт загрузка первого кадра (IsBuffering), панель управления и стрелки
    /// прячутся, чтобы чёрный cover перекрывал всё ForegroundWindow и белый фон
    /// нативного HWND не просвечивал через полупрозрачную панель.
    /// </summary>
    private void UpdateChromeVisibility()
    {
        bool show = _controlsShown && _vm?.IsBuffering != true;
        ControlBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

        // Мини-таймлайн: тонкий индикатор прогресса, когда основная панель скрыта.
        bool showMini = !show
                        && _vm?.IsBuffering != true
                        && _vm?.IsEnded != true
                        && _vm?.CanShowMiniTimeline == true;
        if (_miniTimeline != null)
            _miniTimeline.Visibility = showMini ? Visibility.Visible : Visibility.Collapsed;

        // Устанавливаем курсор на Overlay (а не на UserControl), чтобы он точно применялся
        // над областью видео внутри airspace LibVLCSharp.WPF.
        // Во время буферизации курсор оставляем видимым — переключение короткое, и скрывать
        // его не нужно; при воспроизведении без действий — None (автоскрытие).
        bool hideCursor = !show && _vm?.IsBuffering != true;
        Overlay.Cursor = hideCursor ? Cursors.None : Cursors.Arrow;
        if (Window.GetWindow(this) is { } w)
            w.Cursor = hideCursor ? Cursors.None : Cursors.Arrow;
        UpdateSideNav();
        UpdateInfo();
    }

    // Боковые стрелки перехода между файлами показываются только вместе с панелью управления
    // (и только когда файлов больше одного) — прячутся по таймеру одновременно с ней.
    // Во время буферизации тоже скрываем, чтобы не оставались «дырки» в чёрном cover.
    private void UpdateSideNav()
    {
        var show = _controlsShown
                   && _vm?.ShowFileNavigation == true
                   && _vm?.IsBuffering != true;
        var visibility = show ? Visibility.Visible : Visibility.Collapsed;
        PrevFileButton.Visibility = visibility;
        NextFileButton.Visibility = visibility;
    }

    private void OnToggleChrome()
    {
        if (_controlsShown)
        {
            _controlsShown = false;
            UpdateChromeVisibility();
            _hideTimer.Stop();
            SyncChromeVisible();
        }
        else
        {
            ShowControls();
        }
    }

    /// <summary>Идемпотентно переподписаться на нужный MainViewModel (число += всегда = числу -=).</summary>
    private void AttachMainVm(MainViewModel? vm)
    {
        if (ReferenceEquals(vm, _mainVm)) return; // уже привязаны к этому VM
        if (_mainVm != null) _mainVm.PropertyChanged -= OnMainVmPropertyChanged;
        _mainVm = vm;
        if (_mainVm != null) _mainVm.PropertyChanged += OnMainVmPropertyChanged;
    }

    private void DetachMainVm() => AttachMainVm(null);

    private void OnMainVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsFullScreen)
            || e.PropertyName == nameof(MainViewModel.StatusText)
            || e.PropertyName == nameof(MainViewModel.FolderSummaryText)
            || e.PropertyName == nameof(MainViewModel.ShowFullscreenInfo))
        {
            UpdateInfo();
        }
        else if (e.PropertyName == nameof(MainViewModel.CanToggleClone))
        {
            UpdateCloneButton();
        }
    }

    private void UpdateInfo()
    {
        if (FullscreenInfoBorder == null || InfoText == null) return;
        bool show = _mainVm?.IsFullScreen == true
                    && _controlsShown
                    && _vm?.IsBuffering != true
                    && !string.IsNullOrEmpty(_mainVm.StatusText);

        var summary = _mainVm?.FolderSummaryText;
        if (!string.IsNullOrEmpty(summary))
            InfoText.Text = $"{_mainVm?.StatusText} · {summary}";
        else
            InfoText.Text = _mainVm?.StatusText ?? string.Empty;

        FullscreenInfoBorder.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateCloneButton()
    {
        if (CloneDisplayButton == null) return;
        CloneDisplayButton.Visibility = (_mainVm?.CanToggleClone == true) ? Visibility.Visible : Visibility.Collapsed;
    }
}
