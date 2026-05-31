using System.Windows;
using Prosmotr.Models;
using Prosmotr.Services;
using Prosmotr.ViewModels;
using Wpf.Ui.Controls;

namespace Prosmotr.Views;

/// <summary>Окно «Свойства» в стиле приложения (вместо системного диалога Windows).</summary>
public partial class FilePropertiesWindow : FluentWindow
{
    public FilePropertiesWindow(MediaItem item, LibVlcProvider vlc)
    {
        InitializeComponent();
        var viewModel = new FilePropertiesViewModel(item, vlc);
        DataContext = viewModel;
        _ = viewModel.LoadAsync();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
