using Prosmotr.Services.Torrent;
using Xunit;

namespace Prosmotr.Tests;

/// <summary>Выбор видеофайла в торренте: самый большой файл с видео-расширением.</summary>
public sealed class TorrentFileSelectorTests
{
    [Fact]
    public void SelectVideoFile_Empty_ReturnsNull() =>
        Assert.Null(TorrentFileSelector.SelectVideoFile(Array.Empty<TorrentFileEntry>()));

    [Fact]
    public void SelectVideoFile_NoVideo_ReturnsNull()
    {
        var files = new[] { new TorrentFileEntry("readme.txt", 100), new TorrentFileEntry("cover.jpg", 200) };
        Assert.Null(TorrentFileSelector.SelectVideoFile(files));
    }

    [Fact]
    public void SelectVideoFile_PicksLargestVideo_IgnoringBiggerNonVideo()
    {
        var files = new[]
        {
            new TorrentFileEntry("Movie/movie.mkv", 1_000_000),
            new TorrentFileEntry("Movie/sample.mp4", 50_000),   // sample меньше основного — не выбираем
            new TorrentFileEntry("Movie/extra.bin", 900_000_000) // не видео — игнор
        };
        var selected = TorrentFileSelector.SelectVideoFile(files);
        Assert.NotNull(selected);
        Assert.Equal("Movie/movie.mkv", selected!.Path);
    }

    [Fact]
    public void SelectVideoFile_UppercaseExtension_Selected()
    {
        var files = new[] { new TorrentFileEntry("clip.MKV", 42), new TorrentFileEntry("a.mp4", 10) };
        Assert.Equal("clip.MKV", TorrentFileSelector.SelectVideoFile(files)!.Path);
    }

    [Fact]
    public void SelectVideoFile_VideoInSubfolder_Wins()
    {
        var files = new[]
        {
            new TorrentFileEntry("season/ep1.mp4", 500),
            new TorrentFileEntry("season/ep2.avi", 300)
        };
        Assert.Equal("season/ep1.mp4", TorrentFileSelector.SelectVideoFile(files)!.Path);
    }

    [Fact]
    public void SelectVideoFile_NullEnumerable_ReturnsNull() =>
        Assert.Null(TorrentFileSelector.SelectVideoFile(null!));
}
