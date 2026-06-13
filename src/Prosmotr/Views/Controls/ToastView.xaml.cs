using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Prosmotr.Services.Abstractions;
using Prosmotr.ViewModels;
using Wpf.Ui.Controls;

namespace Prosmotr.Views.Controls;

/// <summary>
/// Самодостаточный тост: подписывается на <see cref="INotificationService"/> (из DI приложения)
/// и анимирует плашку. Используется и в главном окне, и внутри оверлея видео (поверх airspace VLC).
/// </summary>
public partial class ToastView : UserControl
{
    private INotificationService? _service;
    private readonly DispatcherTimer _hideTimer;
    private readonly Queue<NotificationRequest> _queue = new();
    private const int MaxQueueLength = 3;
    private bool _showing;
    private Action? _pendingAction;

    public ToastView()
    {
        InitializeComponent();
        _hideTimer = new DispatcherTimer();
        _hideTimer.Tick += (_, _) => Hide();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_service != null) return;
        var window = Window.GetWindow(this);
        if (window?.DataContext is MainViewModel vm)
        {
            _service = vm.NotificationService;
            if (_service != null) _service.Requested += OnRequested;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_service != null) _service.Requested -= OnRequested;
        _service = null;
        _hideTimer.Stop();
    }

    private void OnRequested(object? sender, NotificationRequest request)
    {
        if (_queue.Count >= MaxQueueLength)
            _queue.Dequeue(); // вытесняем самое старое при спаме

        _queue.Enqueue(request);
        if (!_showing) DequeueAndShow();
    }

    private void DequeueAndShow()
    {
        // Контрол мог выгрузиться (напр. ушли с видео) пока шла анимация Hide —
        // её Completed не должен перезапускать таймер/анимацию на мёртвом контроле.
        if (!IsLoaded) return;
        if (_showing || _queue.Count == 0) return;
        var request = _queue.Dequeue();
        _showing = true;
        Present(request);
    }

    private void Present(NotificationRequest request)
    {
        Icon.Symbol = SymbolFor(request.Kind);
        Root.Background = BrushFor(request.Kind);
        MessageText.Text = request.Message;

        _pendingAction = request.Action;
        if (request.Action != null && !string.IsNullOrEmpty(request.ActionText))
        {
            ActionButton.Content = request.ActionText;
            ActionButton.Visibility = Visibility.Visible;
        }
        else
        {
            ActionButton.Visibility = Visibility.Collapsed;
        }

        Show(request.Duration);
    }

    private void Show(TimeSpan duration)
    {
        _hideTimer.Stop();
        Root.Visibility = Visibility.Visible;
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
        Slide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase() });

        _hideTimer.Interval = duration;
        _hideTimer.Start();
    }

    private void Hide()
    {
        _hideTimer.Stop();
        _showing = false;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
        fade.Completed += (_, _) =>
        {
            Root.Visibility = Visibility.Collapsed;
            DequeueAndShow();
        };
        Root.BeginAnimation(OpacityProperty, fade);
        Slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-14, TimeSpan.FromMilliseconds(200)));
    }

    private void OnActionClick(object sender, RoutedEventArgs e)
    {
        var action = _pendingAction;
        _pendingAction = null;
        Hide();
        action?.Invoke();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Hide();

    private static SymbolRegular SymbolFor(NotificationKind kind) => kind switch
    {
        NotificationKind.Success => SymbolRegular.CheckmarkCircle24,
        NotificationKind.Warning => SymbolRegular.Warning24,
        NotificationKind.Error => SymbolRegular.ErrorCircle24,
        _ => SymbolRegular.Info24
    };

    private static Brush BrushFor(NotificationKind kind)
    {
        var color = kind switch
        {
            NotificationKind.Success => Color.FromRgb(0x1F, 0x6B, 0x3B),
            NotificationKind.Warning => Color.FromRgb(0x7A, 0x5A, 0x12),
            NotificationKind.Error => Color.FromRgb(0x7A, 0x25, 0x25),
            _ => Color.FromRgb(0x2A, 0x2A, 0x2A)
        };
        var brush = new SolidColorBrush(color) { Opacity = 0.97 };
        brush.Freeze();
        return brush;
    }
}
