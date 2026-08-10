using System.IO;

namespace Prosmotr.Services.Torrent;

/// <summary>
/// Пути кэша магнет-стриминга. По умолчанию — %LOCALAPPDATA%\Prosmotr\torrents.
/// Сессия = папка по infoHash (нижний регистр); данные торрента — в подпапке data
/// (saveDirectory движка), служебное — в .cache (metadata/fast-resume MonoTorrent).
/// </summary>
public static class TorrentCachePaths
{
    public static string DefaultCacheDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Prosmotr", "torrents");

    public static string SessionDirectory(string cacheRoot, string infoHashHex) =>
        Path.Combine(cacheRoot, infoHashHex.ToLowerInvariant());

    public static string SaveDirectoryFor(string cacheRoot, string infoHashHex) =>
        Path.Combine(SessionDirectory(cacheRoot, infoHashHex), "data");
}
