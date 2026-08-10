using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Prosmotr.ViewModels;

namespace Prosmotr.Views;

/// <summary>
/// Экран «магнет-стриминг»: фаза загрузки (прогресс из TorrentSession) и фаза
/// воспроизведения (LibVLC играет поток MonoTorrent через StreamMediaInput).
///
/// Airspace-нюансы LibVLC как в VideoViewerView: чёрный cover поднимается ДО привязки
/// MediaPlayer и до старта, чтобы нативный HWND не мигнул белым; MediaPlayer привязывается
/// после отрисовки cover (DispatcherPriority.Render).
/// </summary>
public sealed partial class TorrentStreamView : UserControl
{
    private TorrentStreamViewModel? _vm;
    private bool _isTimelineDragging;
    private bool _wasReadyToPlay;
    /// <summary>Какую панель выбора открыли (audio/subtitle/speed) — повторный клик той же кнопки закрывает.</summary>
    private string? _trackPickerKind;
    private readonly DispatcherTimer _hideTimer;
    private readonly DispatcherTimer _clickTimer;
    private Point _lastMousePosition = new(-1, -1);

    public TorrentStreamView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        // ВАЖНО: панель/оверлей живут в ForegroundWindow LibVLC (отдельное окно над видео) —
        // события мыши в дереве главного окна (UserControl.PreviewMouseMove) над видео НЕ
        // приходят. Подписываемся на сам Overlay, как VideoViewerView.
        Overlay.MouseMove += OnOverlayMouseMove;
        Overlay.PreviewMouseLeftButtonDown += OnOverlayPreviewMouseLeftButtonDown;
        // Клик по видео: один — пауза, двойной — полный экран (как в основном плеере).
        ClickArea.MouseLeftButtonDown += OnClickAreaDown;
        _clickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        _clickTimer.Tick += OnSingleClickElapsed;
        // PageDown (ToggleChromeKey) — как в основном плеере.
        WeakReferenceMessenger.Default.Register<TorrentStreamView, ToggleChromeMessage>(
            this, static (r, _) => r.OnToggleChrome());
        // Автоскрытие панели управления (как в основном плеере): движение мыши показывает,
        // тик через 3 с прячет — только в фазе воспроизведения и не при буферизации/паузе.
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _hideTimer.Tick += (_, _) => HideControlsIfIdle();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        AttachVm(e.NewValue as TorrentStreamViewModel);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachVm(DataContext as TorrentStreamViewModel);
        if (_vm != null)
        {
            // Cover вверх ДО привязки MediaPlayer (см. VideoViewerView: нативное окно
            // мигает белым, если media-операции случаются до отрисовки cover'а).
            UpdateCover();
            Dispatcher.BeginInvoke(new Action(AttachPlayerAndPlay), DispatcherPriority.Render);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _hideTimer.Stop();
        _clickTimer.Stop();
        _clickTimer.Tick -= OnSingleClickElapsed;
        ClickArea.MouseLeftButtonDown -= OnClickAreaDown;
        WeakReferenceMessenger.Default.Unregister<ToggleChromeMessage>(this);
        // Освобождаем плеер сразу при уходе с экрана (паттерн VideoViewerView.OnUnloaded):
        // иначе нативное окно LibVLC останется висеть и держать поток/файл.
        if (_vm != null)
        {
            try { Video.MediaPlayer = null; } catch { }
            _vm.StopAndRelease();
        }
        DetachVm();
    }

        /// <summary>Идемпотентная привязка VM (паттерн AttachMainVm в VideoViewerView — gotcha 5.23).</summary>
    private void AttachVm(TorrentStreamViewModel? vm)
    {
        if (ReferenceEquals(_vm, vm)) return;
        if (_vm != null) _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = vm;
        _wasReadyToPlay = vm?.IsReadyToPlay ?? false;
        if (_vm != null) _vm.PropertyChanged += OnVmPropertyChanged;
        UpdateCover();
        UpdateBufferingPanel();
    }

    private void DetachVm()
    {
        if (_vm != null) _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = null;
        _hideTimer.Stop();
    }

    // --- Автоскрытие панели управления ---

    /// <summary>MouseMove над оверлеем (ForegroundWindow LibVLC) — показывает панель
    /// и перезапускает отсчёт. Минимальный сдвиг 4 px, чтобы не реагировать на дрожание.</summary>
    private void OnOverlayMouseMove(object sender, MouseEventArgs e)
    {
        if (_vm is not { IsReadyToPlay: true }) return;
        var pos = e.GetPosition(Overlay);
        var delta = new Vector(pos.X - _lastMousePosition.X, pos.Y - _lastMousePosition.Y);
        if (delta.Length < 4) return;
        _lastMousePosition = pos;
        ShowControls();
    }

    private void ShowControls()
    {
        if (_vm is { IsReadyToPlay: true })
            ControlBar.Visibility = Visibility.Visible;
        RestartHideTimer();
    }

    private void RestartHideTimer()
    {
        if (_vm is not { AutoHideControls: true }) return;
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void HideControlsIfIdle()
    {
        _hideTimer.Stop();
        if (_vm is not { AutoHideControls: true, IsReadyToPlay: true, IsPlaying: true }) return;
        if (_vm.IsBuffering) return;                 // на буферизации панель скрыта отдельно
        if (TrackPicker.Visibility == Visibility.Visible) return; // открыт выбор — не прятать
        if (_isTimelineDragging) return;             // тянем таймлайн — не прятать
        ControlBar.Visibility = Visibility.Collapsed;
    }

    /// <summary>PageDown (ToggleChromeKey): показать/скрыть панель, как в основном плеере.</summary>
    private void OnToggleChrome()
    {
        if (_vm is not { IsReadyToPlay: true }) return;
        ControlBar.Visibility = ControlBar.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
        _hideTimer.Stop();
        if (ControlBar.Visibility == Visibility.Visible && _vm.AutoHideControls)
            _hideTimer.Start();
    }

    // --- Клик по видео: один — пауза/воспроизведение, двойной — полный экран ---

    private void OnClickAreaDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        _clickTimer.Stop();
        if (e.ClickCount == 2)
        {
            WeakReferenceMessenger.Default.Send(new ToggleFullScreenMessage());
        }
        else
        {
            _clickTimer.Start(); // если за 220 мс не было второго клика — это одиночный
        }
        FocusHostWindow(); // вернуть клавиатурный фокус окну после клика по видео
    }

    private void OnSingleClickElapsed(object? sender, EventArgs e)
    {
        _clickTimer.Stop();
        _vm?.TogglePlayPauseCommand.Execute(null);
    }

    /// <summary>Клик по видео/фону закрывает панель выбора; по кнопкам/панели — обрабатывается ими.
    /// Handled=true, чтобы клик, закрывший панель, НЕ ушёл дальше на ClickArea (не поставил паузу).</summary>
    private void OnOverlayPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (TrackPicker.Visibility != Visibility.Visible) return;
        var point = e.GetPosition(Overlay);
        if (!IsPointInsideOverlay(TrackPicker, point) && !IsPointInsideOverlay(ControlBar, point))
        {
            CloseTrackPicker();
            e.Handled = true;
        }
    }

    private bool IsPointInsideOverlay(FrameworkElement element, Point point)
    {
        var origin = element.TranslatePoint(new Point(0, 0), Overlay);
        return point.X >= origin.X && point.Y >= origin.Y
            && point.X <= origin.X + element.ActualWidth
            && point.Y <= origin.Y + element.ActualHeight;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TorrentStreamViewModel.IsBuffering):
                UpdateCover();
                UpdateBufferingPanel();
                // На буферизации панель прячем (виден оверлей «Докачивается…»),
                // после — показываем и перезапускаем отсчёт автоскрытия.
                if (_vm!.IsBuffering)
                {
                    ControlBar.Visibility = Visibility.Collapsed;
                    _hideTimer.Stop();
                }
                else
                {
                    ShowControls();
                    RestartHideTimer();
                }
                break;
            case nameof(TorrentStreamViewModel.IsPlaying):
                // На паузе панель всегда видна (как в основном плеере).
                if (_vm!.IsPlaying)
                {
                    ShowControls();
                    RestartHideTimer();
                }
                else
                {
                    ShowControls();
                    _hideTimer.Stop();
                }
                break;
            case nameof(TorrentStreamViewModel.IsReadyToPlay):
                UpdateCover();
                // Запускаем плеер только на ПЕРЕХОДЕ false→true. Раньше повторные ре-райзы
                // IsReadyToPlay (от таймера движка) заново звали AttachPlayerAndPlay → пачка
                // Play() в LibVLC → зависание приложения.
                if (_vm is { IsReadyToPlay: true } && !_wasReadyToPlay)
                    Dispatcher.BeginInvoke(new Action(AttachPlayerAndPlay), DispatcherPriority.Render);
                _wasReadyToPlay = _vm?.IsReadyToPlay ?? false;
                break;
        }
    }

    /// <summary>Создать плеер, привязать к VideoView и запустить воспроизведение.
    /// Порядок важен: сначала HWND (Video.MediaPlayer), потом Play — иначе vout уйдёт в отдельное окно.</summary>
    private void AttachPlayerAndPlay()
    {
        if (_vm == null || !_vm.IsReadyToPlay) return;
        _vm.CreatePlayer(); // идемпотентно
        if (_vm.Player == null) return;
        if (Video.MediaPlayer != _vm.Player)
            Video.MediaPlayer = _vm.Player;
        _vm.StartPlayback();
        FocusHostWindow();
    }

    private void UpdateCover()
    {
        if (_vm == null) return;
        // Cover виден, пока буферизация ИЛИ пока плеер ещё не привязан (до первого кадра).
        var show = _vm.IsBuffering || _vm.Player == null;
        SwitchCover.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateBufferingPanel()
    {
        if (_vm == null) return;
        BufferingPanel.Visibility = _vm.IsBuffering && _vm.IsReadyToPlay
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void FocusHostWindow()
    {
        var window = Window.GetWindow(this);
        if (window != null && !window.IsActive)
        {
            window.Activate();
            window.Focus();
        }
    }

    // --- Таймлайн ---

    private void OnTimelineDragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
    {
        _isTimelineDragging = true;
    }

    private void OnTimelineDragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        _isTimelineDragging = false;
        SeekToCurrent();
    }

    private void OnTimelineMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isTimelineDragging) return; // DragCompleted уже обработал
        SeekToCurrent();
    }

    private void SeekToCurrent()
    {
        if (_vm == null || !_vm.IsReadyToPlay) return;
        _vm.SeekToCommand.Execute(Timeline.Value);
    }

    // --- Панель выбора: озвучка / субтитры / скорость ---
    // Вместо ContextMenu: Popup над airspace-окном LibVLC теряет мышь и закрывается при наведении.
    // Панель — обычный WPF-контент внутри оверлея (ForegroundWindow), клики работают как у кнопок.

    private void OnAudioButtonClick(object sender, RoutedEventArgs e)
    {
        if (TrackPicker.Visibility == Visibility.Visible && _trackPickerKind == "audio")
        {
            CloseTrackPicker();
            return;
        }
        if (_vm == null) return;
        var tracks = _vm.GetAudioTracks();
        var entries = new List<(string Text, bool IsCurrent, Action Action)>();
        if (tracks.Count == 0)
        {
            entries.Add(("Нет аудиодорожек", false, () => { }));
        }
        else
        {
            foreach (var t in tracks)
            {
                var id = t.Id;
                entries.Add((t.Name, t.IsCurrent, () => _vm!.SelectAudioTrack(id)));
            }
        }
        ShowTrackPicker(entries, "Аудиодорожка", "audio");
    }

    private void OnSubtitleButtonClick(object sender, RoutedEventArgs e)
    {
        if (TrackPicker.Visibility == Visibility.Visible && _trackPickerKind == "subtitle")
        {
            CloseTrackPicker();
            return;
        }
        if (_vm == null) return;
        var entries = new List<(string Text, bool IsCurrent, Action Action)>();
        foreach (var t in _vm.GetSubtitleTracks())
        {
            var id = t.Id;
            entries.Add((t.Name, t.IsCurrent, () => _vm!.SelectSubtitle(id)));
        }
        ShowTrackPicker(entries, "Субтитры", "subtitle",
            footer: ("Загрузить файл субтитров…", () => _vm?.LoadSubtitleCommand.Execute(null)));
    }

    private void OnSpeedButtonClick(object sender, RoutedEventArgs e)
    {
        if (TrackPicker.Visibility == Visibility.Visible && _trackPickerKind == "speed")
        {
            CloseTrackPicker();
            return;
        }
        if (_vm == null) return;
        var entries = new List<(string Text, bool IsCurrent, Action Action)>();
        foreach (var option in _vm.AvailableRates)
        {
            var value = option.Value;
            entries.Add((option.Label, Math.Abs(_vm.Rate - value) < 0.001f, () => _vm!.SetRate(value)));
        }
        ShowTrackPicker(entries, "Скорость", "speed");
    }

    private void ShowTrackPicker(
        List<(string Text, bool IsCurrent, Action Action)> entries,
        string header,
        string kind,
        (string Text, Action Action)? footer = null)
    {
        _trackPickerKind = kind;
        TrackPickerItems.Children.Clear();

        TrackPickerItems.Children.Add(new TextBlock
        {
            Text = header,
            Foreground = Brushes.White,
            Opacity = 0.7,
            FontSize = 12,
            Margin = new Thickness(10, 4, 10, 6)
        });

        foreach (var (text, isCurrent, action) in entries)
            TrackPickerItems.Children.Add(MakePickerItem(text, isCurrent, action));

        if (footer is var (footerText, footerAction))
        {
            TrackPickerItems.Children.Add(new Separator
            {
                Foreground = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                Margin = new Thickness(4, 6, 4, 6)
            });
            TrackPickerItems.Children.Add(MakePickerItem(footerText, false, footerAction));
        }

        TrackPicker.Visibility = Visibility.Visible;
        TrackPicker.UpdateLayout(); // чтобы ActualWidth был корректен для позиционирования
        PositionTrackPicker();
    }

    /// <summary>Панель — над нажатой кнопкой (по горизонтали — центрируется по кнопке,
    /// с зажимом к краям окна; по вертикали — сразу над панелью управления).</summary>
    private void PositionTrackPicker()
    {
        var button = _trackPickerKind switch
        {
            "audio" => AudioButton,
            "subtitle" => SubtitleButton,
            "speed" => SpeedButton,
            _ => null
        };
        if (button == null || TrackPicker.Visibility != Visibility.Visible) return;
        if (TrackPicker.Parent is not FrameworkElement overlay) return;

        var buttonPos = button.TranslatePoint(new Point(0, 0), overlay);
        var width = TrackPicker.ActualWidth > 0 ? TrackPicker.ActualWidth : 250;
        var left = buttonPos.X + button.ActualWidth / 2 - width / 2;
        var maxLeft = Math.Max(0, overlay.ActualWidth - width);

        TrackPicker.HorizontalAlignment = HorizontalAlignment.Left;
        TrackPicker.Margin = new Thickness(Math.Clamp(left, 0, maxLeft), 0, 0, 10);
    }

    private Button MakePickerItem(string text, bool isCurrent, Action action)
    {
        var btn = new Button
        {
            Content = (isCurrent ? "✓  " : "     ") + text,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(2, 1, 2, 1),
            Background = isCurrent
                ? new SolidColorBrush(Color.FromArgb(70, 90, 200, 255))
                : Brushes.Transparent,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            FontSize = 13,
            Cursor = Cursors.Hand
        };
        btn.Click += (_, _) =>
        {
            CloseTrackPicker();
            action();
        };
        return btn;
    }

    private void CloseTrackPicker()
    {
        TrackPicker.Visibility = Visibility.Collapsed;
        _trackPickerKind = null;
    }

    // --- Клавиши ---

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled) return; // MainWindow уже обработал (хоткеи над airspace VLC)
        if (_vm == null) return;
        switch (e.Key)
        {
            case Key.Space:
                _vm.TogglePlayPauseCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape:
                // Вне полного экрана Esc закрывает сессию (полный экран обрабатывает MainWindow).
                _vm.CloseSessionCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Left:
                _vm.SeekToCommand.Execute(Math.Max(0, _vm.PositionMs - 10_000));
                e.Handled = true;
                break;
            case Key.Right:
                _vm.SeekToCommand.Execute(Math.Min(_vm.LengthMs, _vm.PositionMs + 10_000));
                e.Handled = true;
                break;
        }
    }
}
