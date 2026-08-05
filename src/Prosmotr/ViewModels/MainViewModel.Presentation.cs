using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;

namespace Prosmotr.ViewModels;

/// <summary>Часть MainViewModel: полноэкранный режим, слайд-шоу.</summary>
public sealed partial class MainViewModel
{
    // --- Настройки / режимы ---

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke();

    [RelayCommand]
    private void ToggleFullScreen() => DeferFullScreenTransition(() => IsFullScreen = !IsFullScreen);

    [RelayCommand(CanExecute = nameof(CanToggleClone))]
    private void ToggleCloneDisplay() => _displayTopology.ToggleClone();

    [RelayCommand]
    private void ExitFullScreen() => DeferFullScreenTransition(() => IsFullScreen = false);

    /// <summary>
    /// Переход в/из fullscreen выполняется НЕ синхронно внутри обработчика клика, а после
    /// полного завершения цикла ввода.
    ///
    /// Причина (подтверждена зависанием всего ПК, Event 41 без TDR, AMD Radeon): WPF-кнопка
    /// снимает захват мыши ПОСЛЕ вызова OnClick — т.е. команда исполняется, пока кнопка ещё
    /// держит захват. Синхронный рестайл/ресайз окна (FullScreenHelper.Enter: SetWindowLongPtr,
    /// SetWindowPos, DWM-атрибуты, SetWindowSubclass) при захваченной мыши на фоне живого видео
    /// (нативный D3D11-HWND LibVLC внутри HwndHost) создаёт окно/вводный feedback: Windows шлёт
    /// WM_NCHITTEST окну-владельцу захвата, видео-HWND пересоздаёт swapchain под ресайзом,
    /// ForegroundWindow LibVLCSharp репозиционируется — драйвер вешается намертво.
    /// Горячая клавиша работает, т.к. при ней захвата мыши нет и переход происходит на «тихой»
    /// машине. Отсрочка (и принудительное снятие захвата) делает кнопку эквивалентной хоткею.
    /// </summary>
    private void DeferFullScreenTransition(Action action)
    {
        // Снимаем захват мыши, если кнопка его ещё держит (WPF снимает после OnClick).
        Mouse.Capture(null);
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted)
        {
            action();
            return;
        }
        // Input-приоритет: отработает после текущего события ввода (MouseUp→Click завершён,
        // захват снят, фокус устаканился) — до следующего пользовательского ввода.
        dispatcher.BeginInvoke(action, DispatcherPriority.Input);
    }

    [RelayCommand(CanExecute = nameof(HasItems))]
    private void ToggleSlideshow()
    {
        if (IsSlideshowActive)
        {
            _slideshowTimer.Stop();
            IsSlideshowActive = false;
        }
        else
        {
            _slideshowTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(_settings.Settings.SlideshowIntervalSeconds, 1, 60));
            _slideshowTimer.Start();
            IsSlideshowActive = true;
        }
    }
}
