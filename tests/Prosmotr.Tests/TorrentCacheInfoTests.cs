using System.IO;
using Prosmotr.Services.Torrent;
using Xunit;

namespace Prosmotr.Tests;

/// <summary>Сканирование кэша магнет-стриминга: пути, размеры, пропуск служебного .cache.</summary>
public sealed class TorrentCacheInfoTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "prosmotr-cache-" + Guid.NewGuid().ToString("N"));

    public TorrentCacheInfoTests()
    {
        Directory.CreateDirectory(_temp);
    }

    public void Dispose()
    {
        try { Directory.Delete(_temp, recursive: true); } catch { }
    }

    private void WriteFile(string relative, long bytes)
    {
        var path = Path.Combine(_temp, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = File.Create(path);
        fs.SetLength(bytes); // разреженный файл нужной длины — размер считается правильно
    }

    [Fact]
    public void Scan_Empty_ReturnsZero()
    {
        var info = TorrentCacheInfo.Scan(_temp);
        Assert.Empty(info.Torrents);
        Assert.Equal(0, info.TotalBytes);
    }

    [Fact]
    public void Scan_SumsSizesAndSkipsDotCache()
    {
        WriteFile("abc123/data/Movie.mkv", 1_000_000);
        WriteFile("abc123/data/sub.srt", 5_000);
        WriteFile("def456/data/Series S01E01.mkv", 500_000);
        WriteFile(".cache/fastresume/x.fresume", 1_000_000_000); // служебное — пропускаем

        var info = TorrentCacheInfo.Scan(_temp);

        Assert.Equal(2, info.Torrents.Count);
        Assert.Equal(1_505_000, info.TotalBytes);
        var movie = info.Torrents.First(t => t.FolderHash == "abc123");
        Assert.Equal("Movie.mkv", movie.FileName);
        Assert.Equal(1_005_000, movie.Bytes);
    }

    [Fact]
    public void Scan_NonexistentDir_ReturnsZero()
    {
        var info = TorrentCacheInfo.Scan(Path.Combine(_temp, "nope"));
        Assert.Empty(info.Torrents);
        Assert.Equal(0, info.TotalBytes);
    }
}
