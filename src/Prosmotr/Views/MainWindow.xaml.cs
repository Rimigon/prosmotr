using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Prosmotr.Models;
using Prosmotr.Services;
using Prosmotr.Services.Abstractions;
using Prosmotr.ViewModels;
using Wpf.Ui.Controls;

namespace Prosmotr.Views;

public partial class MainWindow : FluentWindow
{
    private const int WM_KEYDOWN = 0x0100;

    private readonly MainViewModel _vm;
    private readonly IServiceProvider _services;

    private readonly ISettingsService _settings;
    private WindowState _prevState = WindowState.Maximized;
    private WindowStyle _prevStyle = WindowStyle.SingleBorderWindow;
    private ResizeMode _prevResize = ResizeMode.CanResize;
    private WindowChrome? _prevChrome;
    private double _prevLeft;
    private double _prevTop;
    private double _prevWidth;
    private double _prevHeight;
    private Brush? _prevWindowBackground;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int GWL_STYLE = -16;
    private const uint WS_CAPTION = 0x00C00000;
    private const uint WS_THICKFRAME = 0x00040000;
    private const uint WS_MAXIMIZEBOX = 0x00010000;
    private const uint WS_MINIMIZEBOX = 0x00020000;
    private static readonly IntPtr HWND_TOP = new IntPtr(0);
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);
    private const int DWMWA_COLOR_DEFAULT = unchecked((int)0xFFFFFFFF);
    private const int DWMWCP_DEFAULT = 0;
    private const int DWMWCP_DONOTROUND = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;
    }

    // Подкласс окна для полноэкранного режима — перехватываем WM_NCHITTEST,
    // чтобы TitleBar / WindowChrome не оставляли возможность ресайза по краям.
    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, uint uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, uint uIdSubclass);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private delegate IntPtr SubclassProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData);

    private const uint FULLSCREEN_SUBCLASS_ID = 42;
    private const int WM_NCHITTEST = 0x0084;
    private const int HTCLIENT = 1;
    private SubclassProc? _fullScreenSubclassDelegate;

    private uint _prevWindowStyle;

    // Пока открыт модальный диалог (настройки/свойства) — глобальный перехват клавиш не работает.
    private bool _suspendHotkeys;

    // Автоскрытие «плавающих» элементов фото (нижняя панель + боковые стрелки) по бездействию.
    private readonly DispatcherTimer _chromeHideTimer;
    private Point _lastMousePos = new Point(-1, -1);

    public MainWindow(MainViewModel viewModel, IServiceProvider services)
    {
        InitializeComponent();

        _vm = viewModel;
        _services = services;
        _settings = services.GetRequiredService<ISettingsService>();
        DataContext = _vm;

        _vm.SettingsRequested += OpenSettings;
        _vm.PropertiesRequested += OpenProperties;
        _vm.PropertyChanged += OnViewModelPropertyChanged;

        _chromeHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _chromeHideTimer.Tick += OnChromeHideTick;

        Loaded += OnLoaded;
        // Движение мыши показывает элементы фото и перезапускает таймер скрытия (туннелирование —
        // ловим до того, как событие «съест» ZoomBorder при панорамировании).
        PreviewMouseMove += OnPreviewMouseMove;
        // Горячие клавиши ловим двумя путями:
        //  • PreviewKeyDown (туннелирование) — когда фокус у элемента WPF внутри окна;
        //  • ComponentDispatcher.ThreadPreprocessMessage — перехват WM_KEYDOWN на уровне потока,
        //    чтобы клавиши работали ДАЖЕ когда фокус перехватило нативное окно видео LibVLC
        //    (иначе Delete/громкость/перемотка не срабатывают, пока не кликнешь по видео).
        PreviewKeyDown += OnPreviewKeyDown;
        ComponentDispatcher.ThreadPreprocessMessage += OnThreadPreprocessMessage;
        Closed += (_, _) => ComponentDispatcher.ThreadPreprocessMessage -= OnThreadPreprocessMessage;
        DragOver += OnDragOver;
        Drop += OnDrop;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateStripLayout();
        Focus();
        Keyboard.Focus(this);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.IsFullScreen):
                ApplyFullScreen(_vm.IsFullScreen);
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
        if (pos == _lastMousePos) return; // игнорируем «ложные» MouseMove от перестроения визуального дерева
        _lastMousePos = pos;

        if (!_vm.ChromeVisible) _vm.ChromeVisible = true;
        Cursor = Cursors.Arrow;
        RestartChromeTimer();
    }

    private void OnChromeHideTick(object? sender, EventArgs e)
    {
        _chromeHideTimer.Stop();
        if (_vm.CurrentContent is not ImageViewerViewModel) return;

        _vm.ChromeVisible = false;
        Cursor = Cursors.None; // прячем курсор вместе с элементами управления
    }

    // Смена контента / новое фото: показать элементы и заново запустить отсчёт (для фото),
    // для видео и пустого экрана — остановить таймер и держать элементы видимыми.
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
        _chromeHideTimer.Start();
    }

    // --- Полноэкранный режим (через окно, не Popup — иначе ломается оверлей видео) ---

    private IntPtr FullScreenSubclassProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData)
    {
        if (msg == WM_NCHITTEST)
        {
            // Весь экран — клиентская область, никакого ресайза и рамок.
            return (IntPtr)HTCLIENT;
        }
        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    private void ApplyFullScreen(bool on)
    {
        if (on)
        {
            _prevState = WindowState;
            _prevStyle = WindowStyle;
            _prevResize = ResizeMode;
            _prevChrome = WindowChrome.GetWindowChrome(this);
            _prevLeft = Left;
            _prevTop = Top;
            _prevWidth = Width;
            _prevHeight = Height;
            _prevWindowBackground = Background;

            // Полностью убираем WindowChrome — в полноэкранном он не нужен
            // и оставляет невидимые границы (GlassFrameThickness / ResizeBorderThickness).
            WindowChrome.SetWindowChrome(this, null);

            // Переводим в Normal, чтобы WPF не мешала ручному позиционированию.
            WindowState = WindowState.Normal;

            var hwnd = new WindowInteropHelper(this).Handle;

            // Убираем WS_CAPTION и WS_THICKFRAME напрямую у HWND.
            var style = (uint)GetWindowLong(hwnd, GWL_STYLE);
            _prevWindowStyle = style;
            var newStyle = style & ~(WS_CAPTION | WS_THICKFRAME | WS_MAXIMIZEBOX | WS_MINIMIZEBOX);
            SetWindowLong(hwnd, GWL_STYLE, (int)newStyle);

            // Убираем DWM-рамку и цветную границу Windows 11 (иначе остаются белые полосы).
            var margins = new MARGINS { cxLeftWidth = 0, cxRightWidth = 0, cyTopHeight = 0, cyBottomHeight = 0 };
            DwmExtendFrameIntoClientArea(hwnd, ref margins);

            int noColor = DWMWA_COLOR_NONE;
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref noColor, Marshal.SizeOf(typeof(int)));

            int noRound = DWMWCP_DONOTROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref noRound, Marshal.SizeOf(typeof(int)));

            // Растягиваем на весь текущий монитор (физические пиксели).
            var hMonitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf(typeof(MONITORINFO)) };
            if (hMonitor != IntPtr.Zero && GetMonitorInfo(hMonitor, ref mi))
            {
                SetWindowPos(hwnd, HWND_TOP,
                    mi.rcMonitor.Left, mi.rcMonitor.Top,
                    mi.rcMonitor.Right - mi.rcMonitor.Left,
                    mi.rcMonitor.Bottom - mi.rcMonitor.Top,
                    SWP_FRAMECHANGED | SWP_NOZORDER | SWP_SHOWWINDOW);
            }
            else
            {
                SetWindowPos(hwnd, HWND_TOP, 0, 0,
                    (int)SystemParameters.PrimaryScreenWidth,
                    (int)SystemParameters.PrimaryScreenHeight,
                    SWP_FRAMECHANGED | SWP_NOZORDER | SWP_SHOWWINDOW);
            }

            // Подкласс — перехватываем WM_NCHITTEST выше всех WPF-хуков.
            _fullScreenSubclassDelegate = FullScreenSubclassProc;
            SetWindowSubclass(hwnd, _fullScreenSubclassDelegate, FULLSCREEN_SUBCLASS_ID, IntPtr.Zero);

            Background = System.Windows.Media.Brushes.Black;
        }
        else
        {
            var hwnd = new WindowInteropHelper(this).Handle;

            if (_fullScreenSubclassDelegate != null)
            {
                RemoveWindowSubclass(hwnd, _fullScreenSubclassDelegate, FULLSCREEN_SUBCLASS_ID);
                _fullScreenSubclassDelegate = null;
            }

            SetWindowLong(hwnd, GWL_STYLE, (int)_prevWindowStyle);
            SetWindowPos(hwnd, HWND_TOP, 0, 0, 0, 0,
                SWP_FRAMECHANGED | SWP_NOZORDER | SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);

            int defaultColor = DWMWA_COLOR_DEFAULT;
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref defaultColor, Marshal.SizeOf(typeof(int)));

            int defaultRound = DWMWCP_DEFAULT;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref defaultRound, Marshal.SizeOf(typeof(int)));

            WindowChrome.SetWindowChrome(this, _prevChrome);
            WindowState = _prevState;
            Left = _prevLeft;
            Top = _prevTop;
            Width = _prevWidth;
            Height = _prevHeight;
            Background = _prevWindowBackground;
        }

        // Форсируем WPF layout pass, чтобы ForegroundWindow LibVLCSharp.WPF
        // синхронизировал позицию overlay-окна после Win32-изменений окна.
        Dispatcher.BeginInvoke(new Action(UpdateLayout), DispatcherPriority.Render);
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
        var vlc = _services.GetRequiredService<LibVlcProvider>();
        var window = new FilePropertiesWindow(item, vlc) { Owner = this };
        _suspendHotkeys = true;
        try { window.ShowDialog(); }
        finally { _suspendHotkeys = false; }
    }

    // --- Горячие клавиши ---

    // Перехват на уровне потока: ловит WM_KEYDOWN даже когда фокус у нативного окна видео VLC,
    // которое в обычном WPF-роутинге «съедает» клавиши (тогда PreviewKeyDown не срабатывает).
    private void OnThreadPreprocessMessage(ref MSG msg, ref bool handled)
    {
        if (handled || msg.message != WM_KEYDOWN) return;
        var key = KeyInterop.KeyFromVirtualKey(msg.wParam.ToInt32());
        if (key != Key.None && TryHandleHotkey(key))
            handled = true; // обработали — дальше по цепочке (TranslateMessage/DispatchMessage) не пускаем
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (TryHandleHotkey(e.Key)) e.Handled = true;
    }

    /// <summary>Единая обработка горячих клавиш. Возвращает true, если клавиша обработана.</summary>
    private bool TryHandleHotkey(Key key)
    {
        if (_suspendHotkeys) return false;

        // Когда фокус в выпадающем списке/ползунке/поле ввода — отдаём навигационные клавиши ему.
        var focused = Keyboard.FocusedElement;
        bool inControl = focused is ComboBox || focused is Slider
            || focused is System.Windows.Controls.Primitives.Thumb
            || focused is System.Windows.Controls.TextBox;
        bool isNavKey = key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Space
            or Key.OemOpenBrackets or Key.OemCloseBrackets or Key.OemPlus or Key.OemMinus
            or Key.Add or Key.Subtract;
        if (inControl && isNavKey) return false;

        // Клавиша закрытия программы (настраивается).
        if (Enum.TryParse<Key>(_settings.Settings.ExitKey, out var exitKey) && key == exitKey)
        {
            Close();
            return true;
        }

        // Клавиша скрытия/показа элементов управления (настраивается).
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
                // В полноэкранном режиме на видео — шаг назад по видео; иначе предыдущий файл.
                if (_vm.IsFullScreen && _vm.CurrentContent is VideoViewerViewModel videoBack)
                    videoBack.StepBackwardCommand.Execute(null);
                else if (_vm.PreviousCommand.CanExecute(null))
                    _vm.PreviousCommand.Execute(null);
                return true;
            case Key.Right:
                if (_vm.IsFullScreen && _vm.CurrentContent is VideoViewerViewModel videoFwd)
                    videoFwd.StepForwardCommand.Execute(null);
                else if (_vm.NextCommand.CanExecute(null))
                    _vm.NextCommand.Execute(null);
                return true;
            case Key.Delete:
                if (_vm.DeleteCommand.CanExecute(null)) _vm.DeleteCommand.Execute(null);
                return true;
            case Key.F:
                _vm.ToggleFullScreenCommand.Execute(null);
                return true;
            case Key.Escape:
                if (_vm.IsFullScreen) { _vm.ExitFullScreenCommand.Execute(null); return true; }
                return false;
        }

        // Клавиши, относящиеся к текущему видео.
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
