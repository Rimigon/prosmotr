using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Prosmotr.ViewModels;

/// <summary>Placeholder shown in the main window while a video is playing in Picture-in-Picture mode.</summary>
public sealed partial class PictureInPicturePlaceholderViewModel : ViewModelBase
{
    private readonly Action? _onRestore;
    private readonly Action? _onClose;

    public PictureInPicturePlaceholderViewModel(Action? onRestore, Action? onClose)
    {
        _onRestore = onRestore;
        _onClose = onClose;
    }

    [RelayCommand]
    private void Restore() => _onRestore?.Invoke();

    [RelayCommand]
    private void ClosePip() => _onClose?.Invoke();
}
