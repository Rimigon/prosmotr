using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Prosmotr.Infrastructure;

namespace Prosmotr.Services.Torrent;

/// <summary>
/// Bootstrap-узлы DHT своими руками. Причина: MonoTorrent умеет ровно один жёстко зашитый
/// хост (`router.bittorrent.com`, см. DhtEngine Tasks/InitialiseTask), а тот сейчас не отвечает;
/// если DNS к нему ещё и режется провайдером, движок остаётся без узлов вообще и перестаёт искать
/// пиров — magnet висит в «Поиск пиров…». Поэтому подкладываем узлы в DhtEngine ДО старта движка:
/// при непустом списке MonoTorrent вообще не трогает свой единственный хост (DhtEngine.Add в
/// состоянии NotReady кладёт их в PendingNodes).
/// </summary>
internal static class DhtBootstrap
{
    /// <summary>Публичные bootstrap-хосты DHT с их портами (разные проекты слушают на разных).</summary>
    private static readonly (string Host, int Port)[] Hosts =
    {
        ("router.bittorrent.com", 6881),
        ("dht.transmissionbt.com", 6881),
        ("dht.libtorrent.org", 25401),
        ("router.utorrent.com", 6881),
        ("dht.aelitis.com", 6881),
        ("dht.bt.am", 6881),
    };

    /// <summary>Статические IP тех же bootstrap-узлов — только те, что реально отвечают
    /// на UDP-пинг DHT. Смысл статики: `router.bittorrent.com` и `router.utorrent.com` сейчас
    /// не отвечают вовсе, а DNS части хостов режется провайдером. Если положиться только на DNS
    /// и резолв оставит ОДИН живой узел, DHT застынет в Initialising навсегда (проверено: 1 узел →
    /// 0 узлов в таблице и через 20 с, а 3 живых → 45-58 узлов за 2 с). Поэтому статика — основа,
    /// DNS — добор на случай смены адресов. Адреса стоит перепроверять при жалобах на скорость
    /// (UDP-пинг DHT-запросом); см. AGENTS 5.36.</summary>
    private static readonly (string Ip, int Port)[] StaticNodes =
    {
        ("212.129.33.59", 6881),
        ("87.98.162.88", 6881),
        ("185.157.221.247", 25401),
    };

    /// <summary>Собрать seed-узлы: проверенные статические IP + свежий DNS-резолв публичных
    /// хостов. Никогда не бросает: недоступные хосты просто пропускаются. Пустой результат —
    /// не кэшировать, чтобы следующая попытка (например, после поднятия VPN) сработала.</summary>
    public static async Task<IReadOnlyList<ReadOnlyMemory<byte>>> ResolveAsync(TimeSpan timeout)
    {
        var tasks = Hosts.Select(ResolveHostAsync).ToArray();
        try
        {
            await Task.WhenAll(tasks).WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            AppLog.Write("[Torrent] DHT bootstrap: DNS timeout");
        }
        catch (Exception ex)
        {
            AppLog.Error("DhtBootstrap.Resolve", ex);
        }

        var nodes = new List<ReadOnlyMemory<byte>>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (ip, port) in StaticNodes)
            if (IPAddress.TryParse(ip, out var address) && seen.Add($"{ip}:{port}"))
                nodes.Add(CompactNode(address, port));

        foreach (var task in tasks)
        {
            if (!task.IsCompletedSuccessfully) continue;
            foreach (var (address, port) in task.Result)
                if (seen.Add($"{address}:{port}"))
                    nodes.Add(CompactNode(address, port));
        }

        AppLog.Write($"[Torrent] DHT bootstrap nodes: {nodes.Count} (static {StaticNodes.Length})");
        return nodes;
    }

    private static async Task<List<(IPAddress Address, int Port)>> ResolveHostAsync((string Host, int Port) target)
    {
        var nodes = new List<(IPAddress, int)>();
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(target.Host).ConfigureAwait(false);
            foreach (var address in addresses)
                if (address.AddressFamily == AddressFamily.InterNetwork)
                    nodes.Add((address, target.Port));
        }
        catch (Exception ex)
        {
            // Блокировка DNS провайдером / нет сети — просто пропускаем хост.
            AppLog.Write($"[Torrent] DHT bootstrap {target.Host}: {ex.GetType().Name}");
        }
        return nodes;
    }

    /// <summary>Compact node, 26 байт: 20 байт node id + 4 байта IPv4 (network order) +
    /// 2 байта порт (big-endian). Ровно тот формат, который пишет/читает MonoTorrent
    /// (Node.CompactPort / Node.FromCompactNode), а не «как в BEP» на глаз.
    /// Node id произвольный: DHT отвечает и на незнакомый id, потом роутинг-таблица сама
    /// заменит его на настоящий из ответа.</summary>
    internal static ReadOnlyMemory<byte> CompactNode(IPAddress address, int port)
    {
        var buffer = new byte[26];
        Random.Shared.NextBytes(buffer.AsSpan(0, 20));
        address.GetAddressBytes().CopyTo(buffer, 20);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(24, 2), (ushort)port);
        return buffer;
    }
}
