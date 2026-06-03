using CommunityToolkit.Mvvm.Input;

namespace Prosmotr.ViewModels;

/// <summary>Часть MainViewModel: навигация по файлам галереи.</summary>
public sealed partial class MainViewModel
{
    // --- Навигация ---

    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private void Next() => _nav.MoveNext();

    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private void Previous() => _nav.MovePrevious();

    private bool CanNavigate => _nav.CanGoNext;
}
