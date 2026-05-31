using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Prosmotr.Models;
using Prosmotr.Services.Abstractions;

namespace Prosmotr.ViewModels;

/// <summary>Пустое состояние: приглашение перетащить файл и кнопки открытия + недавние.</summary>
public sealed partial class EmptyStateViewModel : ViewModelBase, IDisposable
{
    private readonly IRecentFilesService _recent;
    private readonly Func<Task> _openFile;
    private readonly Func<Task> _openFolder;
    private readonly Func<string, Task> _openPath;

    public ObservableCollection<RecentEntry> RecentItems { get; } = new();

    public bool HasRecent => RecentItems.Count > 0;

    public EmptyStateViewModel(
        IRecentFilesService recent,
        Func<Task> openFile,
        Func<Task> openFolder,
        Func<string, Task> openPath)
    {
        _recent = recent;
        _openFile = openFile;
        _openFolder = openFolder;
        _openPath = openPath;

        _recent.Changed += OnRecentChanged;
        RefreshRecent();
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

    [RelayCommand] private Task OpenFile() => _openFile();
    [RelayCommand] private Task OpenFolder() => _openFolder();

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
