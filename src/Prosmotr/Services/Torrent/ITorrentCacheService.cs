namespace Prosmotr.Services.Torrent;

/// <summary>Сервис кэша магнет-стриминга: информация и очистка (общий для кнопки в панели,
/// диалога кэша и кнопки в настройках).</summary>
public interface ITorrentCacheService
{
    /// <summary>Снимок кэша (путь, размер, список раздач).</summary>
    TorrentCacheInfo GetInfo();

    /// <summary>Закрыть активную сессию, удалить папку кэша и записи positions.json с путями в неё.</summary>
    Task ClearAsync();
}
