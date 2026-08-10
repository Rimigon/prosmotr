using Prosmotr.Infrastructure;

namespace Prosmotr.Services.Torrent;

/// <summary>
/// Описание файла внутри торрента — чистая проекция MonoTorrent.ITorrentManagerFile,
/// чтобы селектор тестировался без сети и без нативных зависимостей.
/// </summary>
public sealed record TorrentFileEntry(string Path, long Length);

/// <summary>
/// Выбор видеофайла для воспроизведения: самый большой файл с видео-расширением.
/// В v1 торренты — в основном одиночные фильмы; сезонные папки вне скоупа — берём максимум.
/// </summary>
public static class TorrentFileSelector
{
    public static TorrentFileEntry? SelectVideoFile(IEnumerable<TorrentFileEntry>? files)
    {
        if (files == null) return null;

        TorrentFileEntry? best = null;
        foreach (var file in files)
        {
            var ext = System.IO.Path.GetExtension(file.Path);
            if (!SupportedFormats.VideoExtensions.Contains(ext)) continue;
            if (best == null || file.Length > best.Length) best = file;
        }
        return best;
    }
}
