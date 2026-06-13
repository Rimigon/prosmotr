using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Prosmotr.Services;
using Prosmotr.Services.Abstractions;
using Xunit;

namespace Prosmotr.Tests;

/// <summary>Поведение LRU-кэша изображений, включая инвалидацию по пути (фикс «поворот не виден»).</summary>
public sealed class ImageCacheTests
{
    /// <summary>Фейк-декодер: возвращает крошечный замороженный bitmap без обращения к диску.</summary>
    private sealed class FakeDecoder : IImageDecodingService
    {
        public int Calls;
        public Task<ImageSource?> LoadAsync(string path, int decodePixelWidth = 0, CancellationToken ct = default)
        {
            Interlocked.Increment(ref Calls);
            var bmp = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgr24, null, new byte[3], 3);
            bmp.Freeze();
            return Task.FromResult<ImageSource?>(bmp);
        }
    }

    [Fact]
    public async Task GetAsync_CachesDecodedImage()
    {
        var decoder = new FakeDecoder();
        var cache = new ImageCache(decoder);

        await cache.GetAsync(@"C:\x\a.jpg");
        await cache.GetAsync(@"C:\x\a.jpg"); // повторный — из кэша, без второго декода

        Assert.Equal(1, decoder.Calls);
        Assert.True(cache.TryGetLoaded(@"C:\x\a.jpg", out var img));
        Assert.NotNull(img);
    }

    [Fact]
    public async Task Invalidate_DropsEntry_SoNextGetReDecodes()
    {
        var decoder = new FakeDecoder();
        var cache = new ImageCache(decoder);

        await cache.GetAsync(@"C:\x\a.jpg");
        Assert.True(cache.TryGetLoaded(@"C:\x\a.jpg", out _));

        cache.Invalidate(@"C:\x\a.jpg"); // имитация перезаписи файла (сохранение поворота)

        Assert.False(cache.TryGetLoaded(@"C:\x\a.jpg", out _)); // устаревшая копия выброшена
        await cache.GetAsync(@"C:\x\a.jpg");
        Assert.Equal(2, decoder.Calls); // повторный декод с диска
    }

    [Fact]
    public void TryGetLoaded_Miss_ReturnsFalse()
    {
        var cache = new ImageCache(new FakeDecoder());
        Assert.False(cache.TryGetLoaded(@"C:\x\never.jpg", out var img));
        Assert.Null(img);
    }
}
