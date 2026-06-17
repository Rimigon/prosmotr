using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Prosmotr.Infrastructure;
using Prosmotr.Models;
using Prosmotr.Services.Abstractions;
using Prosmotr.ViewModels;
using Wpf.Ui.Controls;

namespace Prosmotr.Views;

public partial class MainWindow : FluentWindow
{
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_DISPLAYCHANGE = 0x007E;

    private readonly MainViewModel _vm;
    private readonly IServiceProvider _services;
    private readonly ISettingsService _settings;
    private readonly IDisplayTopologyService _displayTopology;
    private readonly DispatcherTimer _chromeHideTimer;
    private Point _lastMousePos = new Point(-1, -1);

    private FullScreenHelper.State? _fsState;
    private HwndSource? _hwndSource;

    // Пока открыт модальный диалог (настройки/свойства) — глобальный перехват клавиш не работает.
    private bool _suspendHotkeys;

    public MainWindow(MainViewModel viewModel, IServiceProvider services)
    {
        InitializeComponent();

        _vm = viewModel;
        _services = services;
        _settings = services.GetRequiredService<ISettingsService>();
        _displayTopology = services.GetRequiredService<IDisplayTopologyService>();
        DataContext = _vm;

        _vm.SettingsRequested += OpenSettings;
        _vm.PropertiesRequested += OpenProperties;
        _vm.PropertyChanged += OnViewModelPropertyChanged;

        _chromeHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _chromeHideTimer.Tick += OnChromeHideTick;

        Loaded += OnLoaded;
        PreviewMouseMove += OnPreviewMouseMove;
        PreviewKeyDown += OnPreviewKeyDown;
        ComponentDispatcher.ThreadPreprocessMessage += OnThreadPreprocessMessage;
        Closed += (_, _) =>
        {
            _chromeHideTimer.Tick -= OnChromeHideTick;
            _chromeHideTimer.Stop();
            ComponentDispatcher.ThreadPreprocessMessage -= OnThreadPreprocessMessage;
            if (_hwndSource != null)
            {
                _hwndSource.RemoveHook(WndProcHook);
                _hwndSource = null;
            }
        };
        DragOver += OnDragOver;
        Drop += OnDrop;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateStripLayout();
        Focus();
        Keyboard.Focus(this);

        // Защита от повторного хука: OnLoaded может сработать не один раз.
        if (_hwndSource == null)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (HwndSource.FromHwnd(hwnd) is HwndSource source)
            {
                _hwndSource = source;
                source.AddHook(WndProcHook);
            }
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!e.Cancel)
        {
            try { _displayTopology?.RestoreExtend(); }
            catch (Exception ex) { AppLog.Error("MainWindow.OnClosing", ex); }
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.IsFullScreen):
                if (_vm.IsFullScreen)
                    _fsState = FullScreenHelper.Enter(this);
                else
                    FullScreenHelper.Exit(this, _fsState);
                // Форсируем WPF layout pass, чтобы ForegroundWindow LibVLCSharp.WPF
                // синхронизировал позицию overlay-окна после Win32-изменений окна.
                Dispatcher.BeginInvoke(new Action(UpdateLayout), DispatcherPriority.Render);
                ResetChrome(); // показать элементы и перезапустить отсчёт после смены режима
                break;
            case nameof(MainViewModel.ThumbnailStripPosition):
            case nameof(MainViewModel.ShowThumbnailStrip):
                UpdateStripLayout();
                break;
            case nameof(MainViewModel.CurrentContent):
                ResetChrome(forceShow: false);
                break;
        }
    }

    // --- Автоскрытие элементов фото по бездействию ---

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        // Видео управляет своими элементами и курсором само (VideoViewerView) — не мешаем.
        if (_vm.CurrentContent is not ImageViewerViewModel) return;

        var pos = e.GetPosition(this);
        if (pos == _lastMousePos) return;
        _lastMousePos = pos;

        if (!_vm.ChromeVisible) _vm.ChromeVisible = true;
        Cursor = Cursors.Arrow;
        RestartChromeTimer();
    }

    private void OnChromeHideTick(object? sender, EventArgs e)
    {
        _chromeHideTimer.Stop();
        if (!_settings.Settings.AutoHideControls) return; // автоскрытие отключено настройкой
        if (_vm.CurrentContent is not ImageViewerViewModel) return;

        _vm.ChromeVisible = false;
        Cursor = Cursors.None; // прячем курсор вместе с элементами управления
    }

    private void ResetChrome(bool forceShow = true)
    {
        if (forceShow)
            _vm.ChromeVisible = true;

        Cursor = _vm.ChromeVisible ? Cursors.Arrow : Cursors.None;

        if (_vm.CurrentContent is ImageViewerViewModel)
            RestartChromeTimer();
        else
            _chromeHideTimer.Stop();
    }

    private void RestartChromeTimer()
    {
        _chromeHideTimer.Stop();
        // Если автоскрытие отключено настройкой — не запускаем таймер, хром остаётся видимым.
        if (_settings.Settings.AutoHideControls)
            _chromeHideTimer.Start();
    }

    // --- Размещение ленты миниатюр (снизу / слева) ---

    private void UpdateStripLayout()
    {
        if (_vm.ThumbnailStripPosition == Models.ThumbnailStripPosition.Left)
        {
            Grid.SetRow(Strip, 0);
            Grid.SetColumn(Strip, 0);
            Strip.Orientation = System.Windows.Controls.Orientation.Vertical;
            Strip.Width = 124;
            Strip.Height = double.NaN;
        }
        else
        {
            Grid.SetRow(Strip, 1);
            Grid.SetColumn(Strip, 1);
            Strip.Orientation = System.Windows.Controls.Orientation.Horizontal;
            Strip.Width = double.NaN;
            Strip.Height = 92;
        }
    }

    private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_DISPLAYCHANGE)
        {
            _displayTopology?.OnDisplaySettingsChanged();
        }
        return IntPtr.Zero;
    }

    // --- Настройки ---

    private void OpenSettings()
    {
        var window = _services.GetRequiredService<SettingsWindow>();
        window.Owner = this;
        _suspendHotkeys = true;
        try { window.ShowDialog(); }
        finally { _suspendHotkeys = false; }
    }

    private void OpenProperties(MediaItem item)
    {
        var window = _services.GetRequiredService<FilePropertiesWindow>();
        window.Initialize(item);
        window.Owner = this;
        _suspendHotkeys = true;
        try { window.ShowDialog(); }
        finally { _suspendHotkeys = false; }
    }

    // --- Горячие клавиши ---

    private void OnThreadPreprocessMessage(ref MSG msg, ref bool handled)
    {
        if (handled || msg.message != WM_KEYDOWN) return;
        var key = KeyInterop.KeyFromVirtualKey(msg.wParam.ToInt32());
        if (key != Key.None && TryHandleHotkey(key))
            handled = true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (TryHandleHotkey(e.Key)) e.Handled = true;
    }

    /// <summary>Единая обработка горячих клавиш. Возвращает true, если клавиша обработана.</summary>
    private bool TryHandleHotkey(Key key)
    {
        if (_suspendHotkeys) return false;

        var focused = Keyboard.FocusedElement;
        bool inControl = focused is ComboBox || focused is Slider
            || focused is System.Windows.Controls.Primitives.Thumb
            || focused is System.Windows.Controls.TextBox;
        bool isNavKey = key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Space
            or Key.OemOpenBrackets or Key.OemCloseBrackets or Key.OemPlus or Key.OemMinus
            or Key.Add or Key.Subtract;
        if (inControl && isNavKey) return false;

        if (Enum.TryParse<Key>(_settings.Settings.ExitKey, out var exitKey) && key == exitKey)
        {
            Close();
            return true;
        }

        if (Enum.TryParse<Key>(_settings.Settings.ToggleChromeKey, out var toggleChromeKey) && key == toggleChromeKey)
        {
            if (_vm.CurrentContent is ImageViewerViewModel)
            {
                _vm.ChromeVisible = !_vm.ChromeVisible;
                if (_vm.ChromeVisible)
                {
                    Cursor = Cursors.Arrow;
                    RestartChromeTimer();
                }
                else
                {
                    Cursor = Cursors.None;
                    _chromeHideTimer.Stop();
                }
            }
            else if (_vm.CurrentContent is VideoViewerViewModel)
            {
                WeakReferenceMessenger.Default.Send(new ToggleChromeMessage());
            }
            return true;
        }

        switch (key)
        {
            case Key.Left:
                if (_vm.CurrentContent is VideoViewerViewModel videoBack
                    && (_settings.Settings.ArrowKeysSeekVideo || _vm.IsFullScreen))
                {
                    videoBack.StepBackwardCommand.Execute(null);
                }
                else if (_vm.PreviousCommand.CanExecute(null))
                    _vm.PreviousCommand.Execute(null);
                return true;
            case Key.Right:
                if (_vm.CurrentContent is VideoViewerViewModel videoFwd
                    && (_settings.Settings.ArrowKeysSeekVideo || _vm.IsFullScreen))
                {
                    videoFwd.StepForwardCommand.Execute(null);
                }
                else if (_vm.NextCommand.CanExecute(null))
                    _vm.NextCommand.Execute(null);
                return true;
            case Key.Delete:
            case Key.Decimal:
                if (_vm.DeleteCommand.CanExecute(null))
                {
                    _vm.DeleteCommand.Execute(null);
                    return true;
                }
                return false;
            case Key.F:
                _vm.ToggleFullScreenCommand.Execute(null);
                return true;
            case Key.F12:
                if (_vm.ToggleCloneDisplayCommand.CanExecute(null))
                    _vm.ToggleCloneDisplayCommand.Execute(null);
                return true;
            case Key.Escape:
                if (_vm.IsFullScreen) { _vm.ExitFullScreenCommand.Execute(null); return true; }
                return false;
        }

        if (_vm.CurrentContent is not VideoViewerViewModel video) return false;

        switch (key)
        {
            case Key.Space:
                video.TogglePlayCommand.Execute(null);
                return true;
            case Key.M:
                video.ToggleMuteCommand.Execute(null);
                return true;
            case Key.Up:
                video.VolumeUpCommand.Execute(null);
                return true;
            case Key.Down:
                video.VolumeDownCommand.Execute(null);
                return true;
            case Key.OemOpenBrackets:
            case Key.OemMinus:
            case Key.Subtract:
                video.NudgeRate(-1);
                return true;
            case Key.OemCloseBrackets:
            case Key.OemPlus:
            case Key.Add:
                video.NudgeRate(1);
                return true;
        }
        return false;
    }

    // --- Drag & Drop ---

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            _ = _vm.HandleDropAsync(files);
    }
}
