using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageMagick;
using Prosmotr.Infrastructure;
using Prosmotr.Services.Abstractions;

namespace Prosmotr.Services;

/// <summary>Декодирование изображений: нативный WPF для распространённых форматов, Magick.NET для WEBP/HEIC.</summary>
public sealed class ImageDecodingService : IImageDecodingService
{
    public Task<ImageSource?> LoadAsync(string path, int decodePixelWidth = 0, CancellationToken ct = default) =>
        Task.Run<ImageSource?>(() =>
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                return SupportedFormats.RequiresMagick(path)
                    ? LoadWithMagick(path, decodePixelWidth)
                    : LoadNative(path, decodePixelWidth);
            }
            catch (OperationCanceledException) { throw; }
            catch (OutOfMemoryException) { throw; }
            catch { return null; }
        }, ct);

    private static ImageSource LoadNative(string path, int decodePixelWidth)
    {
        // StreamSource + OnLoad гарантирует синхронную загрузку:
        // Width/Height доступны сразу после EndInit(), без ожидания DownloadCompleted.
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        if (decodePixelWidth > 0) bmp.DecodePixelWidth = decodePixelWidth;
        bmp.StreamSource = fs;
        bmp.EndInit();
        bmp.Freeze(); // можно использовать из любого потока
        return bmp;
    }

    private static ImageSource LoadWithMagick(string path, int decodePixelWidth)
    {
        using var image = new MagickImage(path);
        if (decodePixelWidth > 0 && image.Width > (uint)decodePixelWidth)
        {
            var geo = new MagickGeometry((uint)decodePixelWidth, (uint)decodePixelWidth)
            {
                IgnoreAspectRatio = false // вписать в квадрат, сохранив пропорции
            };
            image.Resize(geo);
        }

        image.Format = MagickFormat.Png;
        using var ms = new MemoryStream();
        image.Write(ms);
        ms.Position = 0;

        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = ms;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }
}
