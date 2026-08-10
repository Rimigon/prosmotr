using Prosmotr.Models;

namespace Prosmotr.Services.Torrent;

/// <summary>
/// Движок магнет-стриминга. Одна активная сессия на приложение (v1); владеет
/// единственным ClientEngine MonoTorrent на процесс.
/// </summary>
public interface ITorrentEngineService
{
    /// <summary>Добавить магнет-ссылку. Возвращает сессию сразу (статус ResolvingMetadata);
    /// инициализация (метаданные → выбор файла → поток) идёт в фоне и обновляет сессию
    /// до ReadyToPlay/Error. Бросает FormatException при невалидной ссылке.</summary>
    Task<TorrentSession> AddMagnetAsync(string magnet, CancellationToken ct);

    /// <summary>Активная сессия (null, если нет).</summary>
    TorrentSession? GetActiveSession();

    /// <summary>Остановить активную сессию: fast-resume сохраняется; данные удаляются
    /// только если включена настройка DeleteTorrentCacheOnExit.</summary>
    Task CloseSessionAsync();

    /// <summary>Остановить все сессии (для App.OnExit).</summary>
    Task ShutdownAsync();
}
