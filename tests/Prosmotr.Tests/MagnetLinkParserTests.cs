using Prosmotr.Infrastructure;
using Xunit;

namespace Prosmotr.Tests;

/// <summary>Валидация/парсинг магнет-ссылок для UI (диалог, буфер обмена, аргументы).</summary>
public sealed class MagnetLinkParserTests
{
    [Fact]
    public void IsValidMagnet_PlainText_False() =>
        Assert.False(MagnetLinkParser.IsValidMagnet("hello world"));

    [Fact]
    public void IsValidMagnet_NullOrEmpty_False()
    {
        Assert.False(MagnetLinkParser.IsValidMagnet(null));
        Assert.False(MagnetLinkParser.IsValidMagnet(string.Empty));
        Assert.False(MagnetLinkParser.IsValidMagnet("   "));
    }

    [Fact]
    public void IsValidMagnet_NonBtihXt_False() =>
        Assert.False(MagnetLinkParser.IsValidMagnet("magnet:?xt=urn:sha1:abc"));

    [Fact]
    public void IsValidMagnet_EmptyHash_False() =>
        Assert.False(MagnetLinkParser.IsValidMagnet("magnet:?xt=urn:btih:"));

    [Fact]
    public void IsValidMagnet_Valid_FullLink() =>
        Assert.True(MagnetLinkParser.IsValidMagnet(
            "magnet:?xt=urn:btih:08ada5a7a6183aae1e09d831df6748d566095a10&dn=Sintel&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337"));

    [Fact]
    public void IsValidMagnet_UppercaseSchemeAndXt_True() =>
        Assert.True(MagnetLinkParser.IsValidMagnet(
            "MAGNET:?XT=URN:BTIH:08ada5a7a6183aae1e09d831df6748d566095a10"));

    [Fact]
    public void IsValidMagnet_XtNotFirst_True()
    {
        // Ленящая валидация: xt может стоять не первым параметром.
        Assert.True(MagnetLinkParser.IsValidMagnet(
            "magnet:?dn=Movie&xt=urn:btih:08ada5a7a6183aae1e09d831df6748d566095a10"));
    }

    [Fact]
    public void TryGetInfoHash_ReturnsLowercaseHash()
    {
        Assert.True(MagnetLinkParser.TryGetInfoHash(
            "magnet:?xt=urn:btih:08ADA5A7A6183AAE1E09D831DF6748D566095A10&dn=X", out var hash));
        Assert.Equal("08ada5a7a6183aae1e09d831df6748d566095a10", hash);
    }

    [Fact]
    public void TryGetInfoHash_Invalid_False()
    {
        Assert.False(MagnetLinkParser.TryGetInfoHash("not a magnet", out _));
        Assert.False(MagnetLinkParser.TryGetInfoHash(null, out _));
    }
}
