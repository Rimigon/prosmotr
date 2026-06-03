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
    private void ToggleFullScreen() => IsFullScreen = !IsFullScreen;

    [RelayCommand(CanExecute = nameof(CanToggleClone))]
    private void ToggleCloneDisplay() => _displayTopology.ToggleClone();

    [RelayCommand]
    private void ExitFullScreen() => IsFullScreen = false;

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
