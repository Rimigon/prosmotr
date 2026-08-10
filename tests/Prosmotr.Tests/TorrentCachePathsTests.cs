using Prosmotr.Services.Torrent;
using Xunit;

namespace Prosmotr.Tests;

/// <summary>Пути кэша магнет-стриминга: %LOCALAPPDATA%\Prosmotr\torrents\<hash>\data.</summary>
public sealed class TorrentCachePathsTests
{
    [Fact]
    public void DefaultCacheDirectory_IsUnderLocalAppData()
    {
        var dir = TorrentCachePaths.DefaultCacheDirectory;
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), dir);
        Assert.EndsWith("torrents", dir);
    }

    [Fact]
    public void SessionDirectory_UsesLowercaseHash()
    {
        var dir = TorrentCachePaths.SessionDirectory(@"C:\cache", "ABC123");
        Assert.Equal(@"C:\cache\abc123", dir);
    }

    [Fact]
    public void SaveDirectoryFor_IsUnderSession()
    {
        var dir = TorrentCachePaths.SaveDirectoryFor(@"C:\cache", "abc");
        Assert.Equal(@"C:\cache\abc\data", dir);
    }
}
