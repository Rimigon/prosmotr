using System.IO;

namespace Prosmotr.Models;

/// <summary>Один медиафайл в галерее текущей папки.</summary>
public sealed class MediaItem
{
    public MediaItem(string fullPath, MediaType mediaType)
    {
        FullPath = fullPath;
        MediaType = mediaType;
    }

    public string FullPath { get; }
    public MediaType MediaType { get; }

    // Метаданные файла (для сортировки и свойств); заполняются при сканировании.
    public long FileSizeBytes { get; set; }
    public DateTime LastWriteTimeUtc { get; set; }
    public DateTime CreationTimeUtc { get; set; }

    public string Extension => Path.GetExtension(FullPath);
    public string FileName => Path.GetFileName(FullPath);
    public string DirectoryPath => Path.GetDirectoryName(FullPath) ?? string.Empty;

    public bool IsVideo => MediaType == MediaType.Video;
    public bool IsImage => MediaType is MediaType.Image or MediaType.AnimatedImage;
    public bool IsAnimated => MediaType == MediaType.AnimatedImage;

    public override string ToString() => FullPath;
}
