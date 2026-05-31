using Prosmotr.Models;

namespace Prosmotr.Services.Abstractions;

/// <summary>Загрузка/сохранение пользовательских настроек (JSON в %APPDATA%).</summary>
public interface ISettingsService
{
    AppSettings Settings { get; }

    /// <summary>Сработает, когда настройки изменены и сохранены (для живого применения, напр. темы).</summary>
    event EventHandler? SettingsChanged;

    /// <summary>Немедленно сохранить и уведомить подписчиков.</summary>
    void Save();

    /// <summary>Отложенное сохранение (debounce) — для частых изменений (громкость, позиция).</summary>
    void SaveDebounced();
}

/// <summary>Список недавних файлов и папок.</summary>
public interface IRecentFilesService
{
    IReadOnlyList<RecentEntry> Items { get; }
    event EventHandler? Changed;
    void Add(string path, bool isFolder);
    void Clear();
}

/// <summary>Хранилище позиций воспроизведения видео (resume).</summary>
public interface IPlaybackPositionStore
{
    PlaybackPosition? Get(string path);
    void Save(string path, long positionMs, long durationMs, float? rate);
    void Remove(string path);
    void Flush();
}

/// <summary>Регистрация ассоциаций файлов (ProgID в HKCU) и открытие системного выбора приложений.</summary>
public interface IFileAssociationService
{
    bool IsRegistered { get; }
    void Register();
    void Unregister();
    /// <summary>Открыть «Приложения по умолчанию» — установить дефолт можно только вручную (защита Win11).</summary>
    void OpenDefaultAppsSettings();
}
