using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Prosmotr.Models;
using Prosmotr.Services.Abstractions;

namespace Prosmotr.ViewModels;

/// <summary>Пустое состояние: приглашение перетащить файл и кнопки открытия + недавние.</summary>
public sealed partial class EmptyStateViewModel : ViewModelBase, IDisposable
{
    private readonly IRecentFilesService _recent;
    private readonly IRecentMagnetsService _recentMagnets;
    private readonly Func<Task> _openFile;
    private readonly Func<Task> _openFolder;
    private readonly Func<string, Task> _openPath;
    private readonly Func<string, Task> _openMagnetLink;
    private readonly Func<Task> _openMagnet;

    public ObservableCollection<RecentEntry> RecentItems { get; } = new();
    public ObservableCollection<RecentMagnetEntry> RecentMagnets { get; } = new();

    public bool HasRecent => RecentItems.Count > 0;
    public bool HasRecentMagnets => RecentMagnets.Count > 0;

    public EmptyStateViewModel(
        IRecentFilesService recent,
        IRecentMagnetsService recentMagnets,
        Func<Task> openFile,
        Func<Task> openFolder,
        Func<string, Task> openPath,
        Func<string, Task> openMagnetLink,
        Func<Task> openMagnet)
    {
        _recent = recent;
        _recentMagnets = recentMagnets;
        _openFile = openFile;
        _openFolder = openFolder;
        _openPath = openPath;
        _openMagnetLink = openMagnetLink;
        _openMagnet = openMagnet;

        _recent.Changed += OnRecentChanged;
        RefreshRecent();
        _recentMagnets.Changed += OnRecentMagnetsChanged;
        RefreshRecentMagnets();
    }

    public void RefreshRecent()
    {
        RecentItems.Clear();
        foreach (var r in _recent.Items.Take(8))
            RecentItems.Add(r);
        OnPropertyChanged(nameof(HasRecent));
    }

    private void OnRecentChanged(object? sender, EventArgs e)
    {
        RefreshRecent();
    }

    public void RefreshRecentMagnets()
    {
        RecentMagnets.Clear();
        foreach (var r in _recentMagnets.Items.Take(8))
            RecentMagnets.Add(r);
        OnPropertyChanged(nameof(HasRecentMagnets));
    }

    private void OnRecentMagnetsChanged(object? sender, EventArgs e)
    {
        RefreshRecentMagnets();
    }

    [RelayCommand] private Task OpenFile() => _openFile();
    [RelayCommand] private Task OpenFolder() => _openFolder();
    [RelayCommand] private Task OpenMagnet() => _openMagnet();

    [RelayCommand]
    private Task OpenRecentMagnet(RecentMagnetEntry? entry) =>
        entry == null ? Task.CompletedTask : _openMagnetLink(entry.Magnet);

    [RelayCommand]
    private void ClearRecentMagnets()
    {
        _recentMagnets.Clear();
        RefreshRecentMagnets();
    }

    [RelayCommand]
    private Task OpenRecent(RecentEntry? entry) =>
        entry == null ? Task.CompletedTask : _openPath(entry.Path);

    [RelayCommand]
    private void ClearRecent()
    {
        _recent.Clear();
        RefreshRecent();
    }

    public void Dispose()
    {
        _recent.Changed -= OnRecentChanged;
    }
}
