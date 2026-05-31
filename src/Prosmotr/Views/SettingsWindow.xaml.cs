using Prosmotr.ViewModels;
using Wpf.Ui.Controls;

namespace Prosmotr.Views;

public partial class SettingsWindow : FluentWindow
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
