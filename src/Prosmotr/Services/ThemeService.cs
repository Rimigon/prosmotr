using System.Windows;
using System.Windows.Media;
using Prosmotr.Models;
using Prosmotr.Services.Abstractions;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Prosmotr.Services;

/// <summary>Применение темы через WPF-UI с автоследованием системной теме Windows.</summary>
public sealed class ThemeService : IThemeService
{
    private Window? _window;
    private bool _subscribed;

    public void Initialize(Window window)
    {
        _window = window;
        if (!_subscribed)
        {
            // Обновлять фон/текст «холста» при смене темы (в т.ч. системной).
            ApplicationThemeManager.Changed += (_, _) => UpdateCanvasBrush();
            _subscribed = true;
        }
    }

    public void Apply(AppTheme theme)
    {
        switch (theme)
        {
            case AppTheme.System:
                ApplicationThemeManager.ApplySystemTheme();
                if (_window != null)
                    SystemThemeWatcher.Watch(_window, WindowBackdropType.None);
                break;

            case AppTheme.Light:
                if (_window != null) SystemThemeWatcher.UnWatch(_window);
                ApplicationThemeManager.Apply(ApplicationTheme.Light, WindowBackdropType.None);
                break;

            case AppTheme.Dark:
                if (_window != null) SystemThemeWatcher.UnWatch(_window);
                ApplicationThemeManager.Apply(ApplicationTheme.Dark, WindowBackdropType.None);
                break;
        }

        UpdateCanvasBrush();
    }

    /// <summary>Чистый чёрный фон/белый текст в тёмной теме; белый фон/чёрный текст — в светлой.</summary>
    private static void UpdateCanvasBrush()
    {
        var app = Application.Current;
        if (app == null) return;

        bool dark = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
        var canvas = new SolidColorBrush(dark ? Colors.Black : Colors.White);
        var foreground = new SolidColorBrush(dark ? Colors.White : Colors.Black);
        // Подсветка кнопок: на чёрном — полупрозрачный белый, на белом — полупрозрачный чёрный.
        var overlay = new SolidColorBrush(dark ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)
                                               : Color.FromArgb(0x22, 0x00, 0x00, 0x00));
        canvas.Freeze();
        foreground.Freeze();
        overlay.Freeze();

        app.Resources["AppCanvasBrush"] = canvas;
        app.Resources["AppCanvasForeground"] = foreground;
        app.Resources["AppOverlayBrush"] = overlay;
    }
}
