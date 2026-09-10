using System.Buffers.Binary;
using System.Net;
using Prosmotr.Services.Torrent;
using Xunit;

namespace Prosmotr.Tests;

/// <summary>Формат compact-node, которым мы кормим DHT. Контракт жёсткий: MonoTorrent читает
/// 26 байт как 20 (id) + 4 (IPv4, network order) + 2 (порт, big-endian) — если порядок байт
/// поедет, узлы молча окажутся мусорными адресами и DHT не заведётся.</summary>
public sealed class DhtBootstrapTests
{
    [Fact]
    public void CompactNode_HasExpectedLayout()
    {
        var node = DhtBootstrap.CompactNode(IPAddress.Parse("1.2.3.4"), 6881).Span;

        Assert.Equal(26, node.Length);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, node.Slice(20, 4).ToArray());
        Assert.Equal(6881, BinaryPrimitives.ReadUInt16BigEndian(node.Slice(24, 2)));
    }

    [Fact]
    public void CompactNode_NodeIdIsNotAllZeroes()
    {
        // Произвольный id (DHT ответит и на незнакомый), но не нулевой — нулевой выглядит
        // как «пустой» узел у части реализаций.
        var node = DhtBootstrap.CompactNode(IPAddress.Parse("8.8.8.8"), 25401).Span;
        Assert.Contains(node.Slice(0, 20).ToArray(), b => b != 0);
    }
}
