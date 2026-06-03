using System.Windows;
using Prosmotr.Models;
using Prosmotr.Services;
using Prosmotr.ViewModels;
using Wpf.Ui.Controls;

namespace Prosmotr.Views;

/// <summary>Окно «Свойства» в стиле приложения (вместо системного диалога Windows).</summary>
public partial class FilePropertiesWindow : FluentWindow
{
    private readonly LibVlcProvider _vlc;

    public FilePropertiesWindow(LibVlcProvider vlc)
    {
        InitializeComponent();
        _vlc = vlc;
    }

    public void Initialize(MediaItem item)
    {
        var viewModel = new FilePropertiesViewModel(item, _vlc);
        DataContext = viewModel;
        _ = viewModel.LoadAsync();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
