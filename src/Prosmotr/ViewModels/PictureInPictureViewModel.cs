using System;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Prosmotr.ViewModels;

/// <summary>VM for the floating Picture-in-Picture window. Proxies commands to the source video VM.</summary>
public sealed partial class PictureInPictureViewModel : ViewModelBase, IDisposable
{
    private readonly VideoViewerViewModel _source;
    private bool _disposed;

    public bool IsPlaying => _source.IsPlaying;
    public double PositionMs => _source.PositionMs;
    public double LengthMs => _source.LengthMs;

    public PictureInPictureViewModel(VideoViewerViewModel source)
    {
        _source = source;
        _source.PropertyChanged += OnSourcePropertyChanged;
    }

    [RelayCommand]
    private void TogglePlay() => _source.TogglePlayCommand.Execute(null);

    [RelayCommand]
    private void Restore() => RestoreRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ClosePip() => CloseRequested?.Invoke(this, EventArgs.Empty);

    public event EventHandler? RestoreRequested;
    public event EventHandler? CloseRequested;

    private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(VideoViewerViewModel.IsPlaying)
                         or nameof(VideoViewerViewModel.PositionMs)
                         or nameof(VideoViewerViewModel.LengthMs))
        {
            OnPropertyChanged(e.PropertyName);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _source.PropertyChanged -= OnSourcePropertyChanged;
    }
}
