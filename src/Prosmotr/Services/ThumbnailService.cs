using System.Windows.Media;
using Prosmotr.Infrastructure;
using Prosmotr.Models;
using Prosmotr.Services.Abstractions;

namespace Prosmotr.Services;

/// <summary>
/// Генерация миниатюр. Сначала пробуем системные миниатюры (как в Проводнике —
/// работают и для видео), при неудаче — собственный декодер изображений.
/// Параллелизм ограничен, чтобы не нагружать диск/ЦП.
/// </summary>
public sealed class ThumbnailService : IThumbnailService
{
    private readonly IImageDecodingService _decoder;
    private readonly SemaphoreSlim _throttle = new(Math.Max(2, Environment.ProcessorCount / 2));

    public ThumbnailService(IImageDecodingService decoder) => _decoder = decoder;

    public async Task<ImageSource?> GetThumbnailAsync(MediaItem item, int size, CancellationToken ct = default)
    {
        try
        {
            await _throttle.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return null; }

        try
        {
            // Системная миниатюра (быстро, кэшируется Windows, работает для видео).
            var shell = await Task.Run(() => ShellThumbnail.TryGet(item.FullPath, size), ct).ConfigureAwait(false);
            if (shell != null) return shell;

            // Fallback для изображений, у которых нет системного thumbnail-провайдера (WEBP/HEIC).
            if (item.IsImage)
                return await _decoder.LoadAsync(item.FullPath, size, ct).ConfigureAwait(false);

            return null;
        }
        catch (OperationCanceledException) { return null; }
        catch { return null; }
        finally
        {
            _throttle.Release();
        }
    }
}
