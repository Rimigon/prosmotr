using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Prosmotr.ViewModels;

namespace Prosmotr.Views;

public partial class PictureInPictureWindow : Window
{
    private readonly DispatcherTimer _hideTimer;
    private bool _isDragging;
    private Point _dragStartPoint;

    public PictureInPictureWindow()
    {
        InitializeComponent();
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _hideTimer.Tick += (_, _) => HidePanel();
        Overlay.MouseMove += OnOverlayMouseMove;
    }

    public void ShowFor(VideoViewerViewModel sourceVm, LibVLCSharp.Shared.MediaPlayer player)
    {
        if (DataContext is PictureInPictureViewModel old) old.Dispose();
        var vm = new PictureInPictureViewModel(sourceVm);
        DataContext = vm;
        vm.RestoreRequested += OnRestoreRequested;
        vm.CloseRequested += OnCloseRequested;
        Show();
        // VideoView (HwndHost) должен отрисовать нативный HWND ДО привязки плеера —
        // иначе плеер не найдёт окно вывода и в PiP останется белый/чёрный экран.
        Dispatcher.BeginInvoke(() =>
        {
            PipVideo.MediaPlayer = player;
            // Плеер мог потерять вывод при отсоединении от основного VideoView;
            // принудительно запускаем/возобновляем отрисовку кадра.
            if (!player.IsPlaying)
                player.Play();
            else
                player.SetVideoTrack(player.VideoTrack);
        }, DispatcherPriority.Render);
        ShowPanel();
    }

    public LibVLCSharp.Shared.MediaPlayer? DetachPlayer()
    {
        var player = PipVideo.MediaPlayer;
        PipVideo.MediaPlayer = null;
        return player;
    }

    public void RaiseRestore() => RestoreRequested?.Invoke(this, EventArgs.Empty);

    private void OnRestoreRequested(object? sender, EventArgs e)
    {
        if (!_isClosed) RestoreRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        if (!_isClosed) CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private bool _isClosed;

    public event EventHandler? RestoreRequested;
    public event EventHandler? CloseRequested;

    private void ShowPanel()
    {
        MiniPanel.Visibility = Visibility.Visible;
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void HidePanel()
    {
        _hideTimer.Stop();
        MiniPanel.Visibility = Visibility.Collapsed;
    }

    private void OnOverlayMouseMove(object sender, MouseEventArgs e)
    {
        ShowPanel();
    }

    private void OnDragAreaMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        _isDragging = true;
        _dragStartPoint = e.GetPosition(this);
        DragArea.CaptureMouse();
    }

    private void OnDragAreaMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;
        var pos = e.GetPosition(this);
        Left += pos.X - _dragStartPoint.X;
        Top += pos.Y - _dragStartPoint.Y;
    }

    private void OnDragAreaMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        DragArea.ReleaseMouseCapture();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _isClosed = true;
        try { PipVideo.MediaPlayer = null; } catch { }
        if (DataContext is PictureInPictureViewModel vm) vm.Dispose();
        base.OnClosing(e);
    }
}
