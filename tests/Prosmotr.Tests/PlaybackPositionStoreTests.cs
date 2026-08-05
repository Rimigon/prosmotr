using Prosmotr.Services;
using Xunit;

namespace Prosmotr.Tests;

/// <summary>Хранилище позиций воспроизведения: round-trip, удаление, персистентность, регистр пути.</summary>
public sealed class PlaybackPositionStoreTests
{
    [Fact]
    public void SaveAndGet_RoundTrip()
    {
        using var dir = new TempDir();
        using var store = new PlaybackPositionStore(dir.Path);

        store.Save(@"C:\v\clip.mp4", positionMs: 42000, durationMs: 120000, rate: 1.5f, audioTrackId: 3, audioTrackName: "Russian");

        var p = store.Get(@"C:\v\clip.mp4");
        Assert.NotNull(p);
        Assert.Equal(42000, p!.PositionMs);
        Assert.Equal(120000, p.DurationMs);
        Assert.Equal(1.5f, p.Rate);
        Assert.Equal(3, p.AudioTrackId);
        Assert.Equal("Russian", p.AudioTrackName);
    }

    [Fact]
    public void Get_UnknownPath_ReturnsNull()
    {
        using var dir = new TempDir();
        using var store = new PlaybackPositionStore(dir.Path);
        Assert.Null(store.Get(@"C:\v\missing.mp4"));
    }

    [Fact]
    public void Remove_DeletesEntry()
    {
        using var dir = new TempDir();
        using var store = new PlaybackPositionStore(dir.Path);
        store.Save(@"C:\v\clip.mp4", 1000, 2000, null, null, null);

        store.Remove(@"C:\v\clip.mp4");

        Assert.Null(store.Get(@"C:\v\clip.mp4"));
    }

    [Fact]
    public void Key_IsCaseInsensitive()
    {
        using var dir = new TempDir();
        using var store = new PlaybackPositionStore(dir.Path);
        store.Save(@"C:\v\Clip.MP4", 5000, 10000, null, null, null);

        Assert.NotNull(store.Get(@"c:\v\clip.mp4"));
    }

    [Fact]
    public void Flush_PersistsAcrossInstances()
    {
        using var dir = new TempDir();

        using (var store = new PlaybackPositionStore(dir.Path))
        {
            store.Save(@"C:\v\clip.mp4", 7777, 99999, 2.0f, 2, "English");
            store.Flush();
        }

        using var reopened = new PlaybackPositionStore(dir.Path);
        var p = reopened.Get(@"C:\v\clip.mp4");
        Assert.NotNull(p);
        Assert.Equal(7777, p!.PositionMs);
        Assert.Equal(2.0f, p.Rate);
        Assert.Equal(2, p.AudioTrackId);
        Assert.Equal("English", p.AudioTrackName);
    }
}
