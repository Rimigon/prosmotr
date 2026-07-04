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
        PipVideo.MediaPlayer = player;
        Show();
        ShowPanel();
    }

    public LibVLCSharp.Shared.MediaPlayer? DetachPlayer()
    {
        var player = PipVideo.MediaPlayer;
        try { PipVideo.MediaPlayer = null; } catch { }
        return player;
    }

    public void RaiseRestore() => RestoreRequested?.Invoke(this, EventArgs.Empty);

    private void OnRestoreRequested(object? sender, EventArgs e) => RestoreRequested?.Invoke(this, EventArgs.Empty);
    private void OnCloseRequested(object? sender, EventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

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
        if (DataContext is PictureInPictureViewModel vm) vm.Dispose();
        base.OnClosing(e);
    }
}
