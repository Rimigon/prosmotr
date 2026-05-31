using System.Threading;
using System.Windows;
using System.Windows.Media;
using Prosmotr.Models;

namespace Prosmotr.Services.Abstractions;

/// <summary>Декодирование изображений: нативный WPF + Magick.NET для WEBP/HEIC.</summary>
public interface IImageDecodingService
{
    /// <summary>Загрузить полноразмерное изображение (frozen ImageSource). decodePixelWidth=0 — оригинал.</summary>
    Task<ImageSource?> LoadAsync(string path, int decodePixelWidth = 0, CancellationToken ct = default);
}

/// <summary>Генерация и кэширование миниатюр для ленты предпросмотра.</summary>
public interface IThumbnailService
{
    Task<ImageSource?> GetThumbnailAsync(MediaItem item, int size, CancellationToken ct = default);
}

/// <summary>Применение темы оформления (WPF-UI) с автоследованием системной теме Windows.</summary>
public interface IThemeService
{
    void Initialize(Window window);
    void Apply(AppTheme theme);
}
