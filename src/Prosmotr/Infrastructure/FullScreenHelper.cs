using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace Prosmotr.Infrastructure;

/// <summary>
/// Утилита для перевода WPF-окна в настоящий полноэкранный режим (borderless) через Win32 API.
/// Изолирует всю нативную логику: подкласс окна, DWN, позиционирование на мониторе.
/// </summary>
public static class FullScreenHelper
{
    /// <summary>Состояние окна до входа в fullscreen. Передаётся в <see cref="Exit"/>.</summary>
    public sealed class State
    {
        public WindowState WindowState;
        public WindowStyle WindowStyle;
        public ResizeMode ResizeMode;
        public WindowChrome? Chrome;
        public double Left;
        public double Top;
        public double Width;
        public double Height;
        public Brush? Background;
        public uint WindowStyleFlags;
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int GWL_STYLE = -16;
    private const uint WS_CAPTION = 0x00C00000;
    private const uint WS_THICKFRAME = 0x00040000;
    private const uint WS_MAXIMIZEBOX = 0x00010000;
    private const uint WS_MINIMIZEBOX = 0x00020000;
    private static readonly IntPtr HWND_TOP = new(0);
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;

    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);
    private const int DWMWA_COLOR_DEFAULT = unchecked((int)0xFFFFFFFF);
    private const int DWMWCP_DEFAULT = 0;
    private const int DWMWCP_DONOTROUND = 1;

    private const uint FULLSCREEN_SUBCLASS_ID = 42;
    private const int WM_NCHITTEST = 0x0084;
    private const int HTCLIENT = 1;

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

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    // SetWindowLongPtr / GetWindowLongPtr — корректные для x64 (IntPtr вместо int).
    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        if (IntPtr.Size == 8)
            return SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
        return new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
    }

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
    {
        if (IntPtr.Size == 8)
            return GetWindowLongPtr64(hWnd, nIndex);
        return new IntPtr(GetWindowLong32(hWnd, nIndex));
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, ref RECT lpRect);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, uint uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, uint uIdSubclass);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private delegate IntPtr SubclassProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData);

    private static readonly Dictionary<IntPtr, SubclassProc> _subclassDelegates = new();

    private static IntPtr FullScreenSubclassProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData)
    {
        if (msg == WM_NCHITTEST)
        {
            // Возвращаем HTCLIENT только для точек ВНУТРИ окна. Безусловный HTCLIENT для любой
            // точки опасен при захваченной мыши во время перехода в fullscreen: Windows шлёт
            // WM_NCHITTEST окну-владельцу захвата на каждый тик позиции, и возврат HTCLIENT
            // для точек за пределами окна (окно в этот момент ещё на старом размере) раздувает
            // шторм WM_NCHITTEST/WM_WINDOWPOSCHANGING — на фоне ресайза D3D11-видео-HWND
            // это риск зависания GPU (полный фриз ПК, см. DeferFullScreenTransition).
            int x = (short)(lParam.ToInt64() & 0xFFFF);
            int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
            var rect = new RECT();
            if (GetWindowRect(hWnd, ref rect) &&
                x >= rect.Left && x < rect.Right && y >= rect.Top && y < rect.Bottom)
                return (IntPtr)HTCLIENT; // весь экран — клиентская область, без resize-границ
        }
        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    /// <summary>
    /// Переводит окно в настоящий полноэкранный режим (без рамок, на весь монитор).
    /// Возвращает состояние до входа — его нужно передать в <see cref="Exit"/>.
    /// </summary>
    public static State Enter(Window window)
    {
        var state = new State
        {
            WindowState = window.WindowState,
            WindowStyle = window.WindowStyle,
            ResizeMode = window.ResizeMode,
            Chrome = WindowChrome.GetWindowChrome(window),
            Left = window.Left,
            Top = window.Top,
            Width = window.Width,
            Height = window.Height,
            Background = window.Background
        };

        // Убираем WindowChrome — иначе остаются невидимые границы.
        WindowChrome.SetWindowChrome(window, null);
        // Переводим в Normal, чтобы WPF не мешала ручному позиционированию.
        window.WindowState = WindowState.Normal;

        var hwnd = new WindowInteropHelper(window).Handle;
        var style = (uint)GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();
        state.WindowStyleFlags = style;
        var newStyle = style & ~(WS_CAPTION | WS_THICKFRAME | WS_MAXIMIZEBOX | WS_MINIMIZEBOX);
        SetWindowLongPtr(hwnd, GWL_STYLE, (IntPtr)(long)newStyle);

        // Убираем DWM-рамку и закругления Windows 11.
        var margins = new MARGINS { cxLeftWidth = 0, cxRightWidth = 0, cyTopHeight = 0, cyBottomHeight = 0 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);

        int noColor = DWMWA_COLOR_NONE;
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref noColor, Marshal.SizeOf(typeof(int)));

        int noRound = DWMWCP_DONOTROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref noRound, Marshal.SizeOf(typeof(int)));

        // Растягиваем на весь текущий монитор.
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

        // Подкласс — перехватываем WM_NCHITTEST выше WPF-хуков TitleBar.
        var subclass = new SubclassProc(FullScreenSubclassProc);
        _subclassDelegates[hwnd] = subclass;
        SetWindowSubclass(hwnd, subclass, FULLSCREEN_SUBCLASS_ID, IntPtr.Zero);

        window.Background = System.Windows.Media.Brushes.Black;

        return state;
    }

    /// <summary>Восстанавливает окно из fullscreen-режима по сохранённому состоянию.</summary>
    public static void Exit(Window window, State? state)
    {
        if (state == null) return;

        var hwnd = new WindowInteropHelper(window).Handle;

        if (_subclassDelegates.TryGetValue(hwnd, out var subclass))
        {
            RemoveWindowSubclass(hwnd, subclass, FULLSCREEN_SUBCLASS_ID);
            _subclassDelegates.Remove(hwnd);
        }

        SetWindowLongPtr(hwnd, GWL_STYLE, (IntPtr)(long)state.WindowStyleFlags);
        SetWindowPos(hwnd, HWND_TOP, 0, 0, 0, 0,
            SWP_FRAMECHANGED | SWP_NOZORDER | SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);

        int defaultColor = DWMWA_COLOR_DEFAULT;
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref defaultColor, Marshal.SizeOf(typeof(int)));

        int defaultRound = DWMWCP_DEFAULT;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref defaultRound, Marshal.SizeOf(typeof(int)));

        WindowChrome.SetWindowChrome(window, state.Chrome);
        window.WindowState = state.WindowState;
        window.Left = state.Left;
        window.Top = state.Top;
        window.Width = state.Width;
        window.Height = state.Height;
        window.Background = state.Background;
    }
}
