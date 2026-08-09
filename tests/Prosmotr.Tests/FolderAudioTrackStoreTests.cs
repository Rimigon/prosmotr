using Prosmotr.Services;
using Xunit;

namespace Prosmotr.Tests;

/// <summary>Хранилище запомненных озвучек папок: round-trip, сброс, персистентность, регистр пути.</summary>
public sealed class FolderAudioTrackStoreTests
{
    [Fact]
    public void SetAndGet_RoundTrip()
    {
        using var dir = new TempDir();
        using var store = new FolderAudioTrackStore(dir.Path);

        store.Set(@"C:\v\Seria\Season 1", audioTrackId: 2, audioTrackName: "Russian");

        var t = store.Get(@"C:\v\Seria\Season 1");
        Assert.NotNull(t);
        Assert.Equal(2, t!.AudioTrackId);
        Assert.Equal("Russian", t.AudioTrackName);
    }

    [Fact]
    public void Get_UnknownFolder_ReturnsNull()
    {
        using var dir = new TempDir();
        using var store = new FolderAudioTrackStore(dir.Path);
        Assert.Null(store.Get(@"C:\v\missing"));
    }

    [Fact]
    public void Clear_DeletesEntry()
    {
        using var dir = new TempDir();
        using var store = new FolderAudioTrackStore(dir.Path);
        store.Set(@"C:\v\Seria\Season 1", 2, "Russian");

        store.Clear(@"C:\v\Seria\Season 1");

        Assert.Null(store.Get(@"C:\v\Seria\Season 1"));
    }

    [Fact]
    public void Key_IsCaseInsensitive()
    {
        using var dir = new TempDir();
        using var store = new FolderAudioTrackStore(dir.Path);
        store.Set(@"C:\v\Seria\SEASON 1", 3, "English");

        Assert.NotNull(store.Get(@"c:\v\seria\season 1"));
    }

    [Fact]
    public void Flush_PersistsAcrossInstances()
    {
        using var dir = new TempDir();

        using (var store = new FolderAudioTrackStore(dir.Path))
        {
            store.Set(@"C:\v\Seria\Season 1", 2, "Russian");
            store.Flush();
        }

        using var reopened = new FolderAudioTrackStore(dir.Path);
        var t = reopened.Get(@"C:\v\Seria\Season 1");
        Assert.NotNull(t);
        Assert.Equal(2, t!.AudioTrackId);
        Assert.Equal("Russian", t.AudioTrackName);
    }
}
