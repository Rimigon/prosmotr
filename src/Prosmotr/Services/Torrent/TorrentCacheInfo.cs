using System.IO;
using IOPath = System.IO.Path;

namespace Prosmotr.Services.Torrent;

/// <summary>Один торрент в кэше (для диалога «Кэш магнет-стриминга»).</summary>
public sealed record TorrentCacheEntry(string FolderHash, string FileName, long Bytes);

/// <summary>Снимок кэша магнет-стриминга: путь, общий размер, список раздач.</summary>
public sealed record TorrentCacheInfo(string Path, long TotalBytes, IReadOnlyList<TorrentCacheEntry> Torrents)
{
    /// <summary>Посчитать содержимое кэша: <c>cacheRoot\&lt;hash&gt;\data\*</c> (служебный .cache пропускаем).</summary>
    public static TorrentCacheInfo Scan(string cacheRoot)
    {
        var entries = new List<TorrentCacheEntry>();
        long total = 0;
        if (Directory.Exists(cacheRoot))
        {
            foreach (var sessionDir in Directory.EnumerateDirectories(cacheRoot))
            {
                var name = IOPath.GetFileName(sessionDir);
                if (name.StartsWith('.')) continue; // .cache — служебное (metadata/fast-resume)

                var dataDir = IOPath.Combine(sessionDir, "data");
                long size = 0;
                string? fileName = null;
                if (Directory.Exists(dataDir))
                {
                    var files = Directory.EnumerateFiles(dataDir, "*", SearchOption.AllDirectories).ToList();
                    fileName = files.Count > 0 ? IOPath.GetFileName(files[0]) : null;
                    foreach (var file in files)
                    {
                        try { size += new FileInfo(file).Length; } catch { /* занят/удалён */ }
                    }
                }

                if (size <= 0 && fileName == null) continue;
                entries.Add(new TorrentCacheEntry(name, fileName ?? name, size));
                total += size;
            }
        }
        return new TorrentCacheInfo(cacheRoot, total, entries);
    }
}
