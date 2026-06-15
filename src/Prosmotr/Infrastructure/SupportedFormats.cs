using System.IO;
using Prosmotr.Models;

namespace Prosmotr.Infrastructure;

/// <summary>Единый источник правды о поддерживаемых расширениях файлов.</summary>
public static class SupportedFormats
{
    // Статичные изображения (нативно в WPF либо через Magick.NET)
    public static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".bmp", ".webp",
        ".tif", ".tiff", ".heic", ".heif", ".ico"
    };

    // Анимированные изображения
    public static readonly HashSet<string> AnimatedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".gif"
    };

    // Видео — проигрываются через LibVLC
    public static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".mkv", ".avi", ".webm", ".m4v",
        ".wmv", ".flv", ".mpg", ".mpeg", ".ts", ".m2ts"
    };

    // Форматы, которые декодируем через Magick.NET вместо нативного WPF.
    // JPEG добавлен из-за бага WPF: BitmapImage падает на JPG с некоторыми
    // embedded ICC color profiles (ColorContext.GetColorContextsHelper).
    // Magick нормализует профиль перед отдачей WPF.
    public static readonly HashSet<string> MagickImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jpe", ".jfif",
        ".webp", ".heic", ".heif"
    };

    public static bool IsSupported(string path) => GetMediaType(path) != MediaType.Unknown;

    public static MediaType GetMediaType(string path)
    {
        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext)) return MediaType.Unknown;
        if (AnimatedImageExtensions.Contains(ext)) return MediaType.AnimatedImage;
        if (ImageExtensions.Contains(ext)) return MediaType.Image;
        if (VideoExtensions.Contains(ext)) return MediaType.Video;
        return MediaType.Unknown;
    }

    public static bool RequiresMagick(string path) =>
        MagickImageExtensions.Contains(Path.GetExtension(path));

    /// <summary>Все поддерживаемые расширения (для диалога открытия и ассоциаций).</summary>
    public static IEnumerable<string> AllExtensions =>
        ImageExtensions.Concat(AnimatedImageExtensions).Concat(VideoExtensions)
            .Distinct(StringComparer.OrdinalIgnoreCase);
}
