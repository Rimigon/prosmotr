using CommunityToolkit.Mvvm.Input;
using Prosmotr.Models;
using Prosmotr.Services.Abstractions;

namespace Prosmotr.ViewModels;

/// <summary>Часть MainViewModel: действия с текущим файлом (проводник, свойства, буфер обмена).</summary>
public sealed partial class MainViewModel
{
    private bool HasCurrent => _nav.Current != null;

    // --- Действия с файлом ---

    [RelayCommand(CanExecute = nameof(HasCurrent))]
    private void ShowInExplorer() => Run(p => _shell.ShowInExplorer(p));

    [RelayCommand(CanExecute = nameof(HasCurrent))]
    private void OpenContainingFolder() => Run(p => _shell.OpenContainingFolder(p));

    [RelayCommand(CanExecute = nameof(HasCurrent))]
    private void CopyPath() => Run(p =>
    {
        _shell.CopyPathToClipboard(p);
        _notify.Show("Путь к файлу скопирован.", NotificationKind.Success);
    });

    [RelayCommand(CanExecute = nameof(HasCurrent))]
    private void OpenWith() => Run(p => _shell.OpenWith(p));

    [RelayCommand(CanExecute = nameof(HasCurrent))]
    private void ShowProperties()
    {
        var cur = _nav.Current;
        if (cur != null) PropertiesRequested?.Invoke(cur);
    }

    private void Run(Action<string> action)
    {
        var cur = _nav.Current;
        if (cur != null) action(cur.FullPath);
    }
}
