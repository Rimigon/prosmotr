using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using ImageMagick;
using LibVLCSharp.Shared;
using Prosmotr.Infrastructure;
using Prosmotr.Models;
using Prosmotr.Services;
using MediaType = Prosmotr.Models.MediaType;

namespace Prosmotr.ViewModels;

/// <summary>Строка свойства «название — значение» (значение может догружаться асинхронно).</summary>
public sealed partial class PropertyRow : ObservableObject
{
    public string Label { get; }
    [ObservableProperty] private string _value;

    public PropertyRow(string label, string value)
    {
        Label = label;
        _value = value;
    }
}

/// <summary>
/// ViewModel окна свойств файла в стиле приложения. Базовые поля (имя, путь, размер, даты)
/// заполняются сразу; «тяжёлые» (размеры изображения, разрешение/длительность видео) —
/// асинхронно (Magick для фото, LibVLC-parse для видео).
/// </summary>
public sealed partial class FilePropertiesViewModel : ViewModelBase
{
    private readonly MediaItem _item;
    private readonly LibVlcProvider _vlc;

    private PropertyRow? _dimensionsRow;
    private PropertyRow? _durationRow;
    private PropertyRow? _frameRateRow;

    public string Title => _item.FileName;

    /// <summary>Иконка вида файла для шапки окна (имя символа WPF-UI).</summary>
    public string KindSymbol => _item.MediaType switch
    {
        MediaType.Video => "Video24",
        MediaType.AnimatedImage => "Gif24",
        _ => "Image24"
    };

    public ObservableCollection<PropertyRow> Rows { get; } = new();

    [ObservableProperty] private bool _isLoading;

    public FilePropertiesViewModel(MediaItem item, LibVlcProvider vlc)
    {
        _item = item;
        _vlc = vlc;
        BuildStaticRows();
    }

    private void BuildStaticRows()
    {
        Rows.Add(new PropertyRow("Имя", _item.FileName));
        Rows.Add(new PropertyRow("Папка", _item.DirectoryPath));
        Rows.Add(new PropertyRow("Тип", TypeText()));
        Rows.Add(new PropertyRow("Размер", SizeText(_item.FileSizeBytes)));
        Rows.Add(new PropertyRow("Изменён", LocalDateText(_item.LastWriteTimeUtc)));
        Rows.Add(new PropertyRow("Создан", LocalDateText(_item.CreationTimeUtc)));

        if (_item.IsImage)
        {
            _dimensionsRow = new PropertyRow("Размеры", "…");
            Rows.Add(_dimensionsRow);
        }
        else if (_item.IsVideo)
        {
            _dimensionsRow = new PropertyRow("Разрешение", "…");
            _durationRow = new PropertyRow("Длительность", "…");
            _frameRateRow = new PropertyRow("Частота кадров", "…");
            Rows.Add(_dimensionsRow);
            Rows.Add(_durationRow);
            Rows.Add(_frameRateRow);
        }
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            if (_item.IsImage) await LoadImageInfoAsync();
            else if (_item.IsVideo) await LoadVideoInfoAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadImageInfoAsync()
    {
        var (w, h) = await Task.Run(() =>
        {
            try
            {
                var info = new MagickImageInfo(_item.FullPath);
                return ((int)info.Width, (int)info.Height);
            }
            catch
            {
                return (0, 0);
            }
        });

        if (_dimensionsRow != null)
            _dimensionsRow.Value = w > 0 ? $"{w} × {h} пикс." : "—";
    }

    private async Task LoadVideoInfoAsync()
    {
        try
        {
            // FromType.FromPath передаёт локальный путь напрямую — корректно для путей
            // со спецсимволами (#, %), на которых new Uri(path) бросает UriFormatException.
            using var media = new Media(_vlc.LibVlc, _item.FullPath, FromType.FromPath);
            await media.Parse(MediaParseOptions.ParseLocal, timeout: 5000);

            if (_durationRow != null)
                _durationRow.Value = media.Duration > 0 ? FormatDuration(media.Duration) : "—";

            var video = media.Tracks.FirstOrDefault(t => t.TrackType == TrackType.Video);
            bool hasVideo = media.Tracks.Any(t => t.TrackType == TrackType.Video);

            if (hasVideo)
            {
                var vt = video.Data.Video;
                if (_dimensionsRow != null)
                    _dimensionsRow.Value = vt.Width > 0 ? $"{vt.Width} × {vt.Height} пикс." : "—";
                if (_frameRateRow != null)
                    _frameRateRow.Value = vt.FrameRateDen > 0
                        ? $"{(double)vt.FrameRateNum / vt.FrameRateDen:0.##} кадр/с"
                        : "—";
            }
            else
            {
                SetVideoUnavailable();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("FileProperties video parse", ex);
            SetVideoUnavailable();
        }
    }

    private void SetVideoUnavailable()
    {
        if (_dimensionsRow != null) _dimensionsRow.Value = "—";
        if (_frameRateRow != null) _frameRateRow.Value = "—";
        if (_durationRow != null) _durationRow.Value = "—";
    }

    private string TypeText()
    {
        var ext = _item.Extension.TrimStart('.').ToUpperInvariant();
        var kind = _item.MediaType switch
        {
            MediaType.Image => "Изображение",
            MediaType.AnimatedImage => "Анимация",
            MediaType.Video => "Видео",
            _ => "Файл"
        };
        return string.IsNullOrEmpty(ext) ? kind : $"{kind} ({ext})";
    }

    private static string SizeText(long bytes)
    {
        string[] units = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
        double size = bytes;
        int u = 0;
        while (size >= 1024 && u < units.Length - 1) { size /= 1024; u++; }
        if (u == 0) return $"{bytes} Б";
        return $"{size.ToString("0.##", CultureInfo.CurrentCulture)} {units[u]} " +
               $"({bytes.ToString("N0", CultureInfo.CurrentCulture)} байт)";
    }

    private static string LocalDateText(DateTime utc)
    {
        if (utc == default) return "—";
        return utc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture);
    }

    private static string FormatDuration(long ms)
    {
        var t = TimeSpan.FromMilliseconds(ms);
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes}:{t.Seconds:00}";
    }
}
