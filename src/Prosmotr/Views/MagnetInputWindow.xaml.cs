using System.Windows;
using Prosmotr.Infrastructure;
using Wpf.Ui.Controls;

namespace Prosmotr.Views;

/// <summary>Диалог вставки магнет-ссылки. Валидация — MagnetLinkParser (ленивая, для UI);
/// авторитетную проверку делает движок (MonoTorrent.MagnetLink.TryParse).</summary>
public partial class MagnetInputWindow : FluentWindow
{
    public string? Magnet { get; private set; }

    public MagnetInputWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => { MagnetBox.Focus(); MagnetBox.SelectAll(); };
    }

    /// <summary>Предзаполнить поле (например, из буфера обмена).</summary>
    public void Prefill(string? magnet)
    {
        MagnetBox.Text = magnet ?? string.Empty;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var text = MagnetBox.Text.Trim();
        if (!MagnetLinkParser.IsValidMagnet(text))
        {
            ErrorText.Text = "Это не похоже на магнет-ссылку (magnet:?xt=urn:btih:…).";
            ErrorText.Visibility = Visibility.Visible;
            MagnetBox.Focus();
            return;
        }
        Magnet = text;
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
