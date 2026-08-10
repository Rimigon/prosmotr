using System.IO;
using Prosmotr.Infrastructure;
using Prosmotr.Services.Abstractions;

namespace Prosmotr.Services.Torrent;

/// <summary>Реализация очистки/информации кэша магнет-стриминга.</summary>
public sealed class TorrentCacheService : ITorrentCacheService
{
    private readonly ITorrentEngineService _torrents;
    private readonly ISettingsService _settings;
    private readonly IPlaybackPositionStore _positions;

    public TorrentCacheService(
        ITorrentEngineService torrents,
        ISettingsService settings,
        IPlaybackPositionStore positions)
    {
        _torrents = torrents;
        _settings = settings;
        _positions = positions;
    }

    private string CacheRoot =>
        _settings.Settings.TorrentCacheDirectory ?? TorrentCachePaths.DefaultCacheDirectory;

    public TorrentCacheInfo GetInfo() => TorrentCacheInfo.Scan(CacheRoot);

    public async Task ClearAsync()
    {
        // Сначала стоп активной сессии: RemoveAsync освобождает файлы, иначе удаление упадёт.
        try { await _torrents.ShutdownAsync(); } catch { }
        try
        {
            if (Directory.Exists(CacheRoot))
                Directory.Delete(CacheRoot, recursive: true);
        }
        catch (Exception ex)
        {
            AppLog.Error("TorrentCacheService.Clear delete", ex);
        }
        // Записи позиций/скоростей/озвучек, ведущие в кэш, больше неактуальны.
        try { _positions.RemoveAll(p => p.Contains("\\torrents\\", StringComparison.OrdinalIgnoreCase)); } catch { }
    }
}
