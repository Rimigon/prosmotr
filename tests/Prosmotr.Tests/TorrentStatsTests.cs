using Prosmotr.Services.Torrent;
using Xunit;

namespace Prosmotr.Tests;

/// <summary>Чистые вычисления для UI загрузки: ETA, формат байт, «позиция за границей скачанного».</summary>
public sealed class TorrentStatsTests
{
    [Theory]
    [InlineData(0, 0, null)]
    [InlineData(100, 0, null)]
    [InlineData(0, 1000, 0L)]
    [InlineData(1_000_000, 500_000, 2L)]
    [InlineData(1_000_000, 100_000, 10L)]
    [InlineData(500, 100, 5L)]
    public void ComputeEtaSeconds_Works(long remaining, long speed, long? expected) =>
        Assert.Equal(expected, TorrentStats.ComputeEtaSeconds(remaining, speed));

    [Theory]
    [InlineData(0, "0 Б")]
    [InlineData(500, "500 Б")]
    [InlineData(2 * 1024, "2.0 КБ")]
    [InlineData(5 * 1024 * 1024, "5.0 МБ")]
    [InlineData(1024L * 1024 * 1024, "1.0 ГБ")]
    [InlineData((long)(1.25 * 1024 * 1024 * 1024), "1.3 ГБ")]
    public void FormatBytes_Works(long bytes, string expected) =>
        Assert.Equal(expected, TorrentStats.FormatBytes(bytes));

    [Fact]
    public void IsBeyondDownloaded_True_WhenPastDownloadedPlusSlack() =>
        Assert.True(TorrentStats.IsBeyondDownloaded(positionMs: 6_000, lengthMs: 10_000, downloadedPercent: 50, slackMs: 500));

    [Fact]
    public void IsBeyondDownloaded_False_WhenWithinDownloaded() =>
        Assert.False(TorrentStats.IsBeyondDownloaded(positionMs: 4_000, lengthMs: 10_000, downloadedPercent: 50, slackMs: 500));

    [Fact]
    public void IsBeyondDownloaded_False_WhenLengthUnknown() =>
        Assert.False(TorrentStats.IsBeyondDownloaded(5_000, 0, 50, 500));

    [Fact]
    public void IsBeyondDownloaded_True_WhenNothingDownloadedYet() =>
        Assert.True(TorrentStats.IsBeyondDownloaded(1_000, 10_000, 0, 500));
}
