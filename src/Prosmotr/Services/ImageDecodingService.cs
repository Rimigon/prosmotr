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
            catch (Exception ex)
            {
                AppLog.Error($"ImageDecodingService.LoadAsync path={path}", ex);
                return null;
            }
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

        // Сбрасываем «проблемные» ICC-профили, которые ломают WPF BitmapImage
        // (ColorContext.GetColorContextsHelper падает на некоторых embedded-профилях).
        // RGB-профили без калибровки WPF понимает сам; CMYK/пустые — нет.
        if (image.ColorSpace == ColorSpace.CMYK)
            image.ColorSpace = ColorSpace.sRGB;
        image.RemoveProfile("icc");

        // Уменьшаем для миниатюры, если ЛЮБОЕ измерение больше бокса (иначе высокое узкое
        // изображение декодировалось бы в полный размер — лишняя память для thumbnail).
        var box = (uint)decodePixelWidth;
        if (decodePixelWidth > 0 && (image.Width > box || image.Height > box))
        {
            var geo = new MagickGeometry(box, box)
            {
                IgnoreAspectRatio = false // вписать в квадрат, сохранив пропорции
            };
            image.Resize(geo);
        }

        // BMP кодируется быстрее PNG и не требует сжатия — меньше latency и пиковая память.
        image.Format = MagickFormat.Bmp;
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
