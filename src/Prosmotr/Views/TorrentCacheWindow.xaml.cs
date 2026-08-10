using System.Windows;
using Prosmotr.Services.Torrent;
using Wpf.Ui.Controls;

namespace Prosmotr.Views;

/// <summary>Диалог «Кэш магнет-стриминга»: путь, размер, список раздач и кнопка очистки.</summary>
public partial class TorrentCacheWindow : FluentWindow
{
    private readonly ITorrentCacheService _cache;

    public TorrentCacheWindow(ITorrentCacheService cache)
    {
        InitializeComponent();
        _cache = cache;
        Loaded += (_, _) => Refresh();
    }

    private void Refresh()
    {
        var info = _cache.GetInfo();
        PathText.Text = "Путь: " + info.Path;
        SummaryText.Text = info.Torrents.Count == 0
            ? "Кэш пуст."
            : $"Всего: {TorrentStats.FormatBytes(info.TotalBytes)} · раздач: {info.Torrents.Count}";
        TorrentsList.ItemsSource = info.Torrents
            .Select(t => new { Name = t.FileName, Size = TorrentStats.FormatBytes(t.Bytes) })
            .ToList();
        ClearButton.IsEnabled = info.Torrents.Count > 0;
        StatusText.Text = string.Empty;
    }

    private async void OnClear(object sender, RoutedEventArgs e)
    {
        ClearButton.IsEnabled = false;
        StatusText.Text = "Очистка…";
        try
        {
            await _cache.ClearAsync();
            StatusText.Text = "Кэш очищен.";
        }
        catch (Exception ex)
        {
            Prosmotr.Infrastructure.AppLog.Error("TorrentCacheWindow.Clear", ex);
            StatusText.Text = "Не удалось очистить кэш (возможно, файл занят).";
        }
        Refresh();
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
