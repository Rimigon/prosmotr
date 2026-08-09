using Prosmotr.Models;

namespace Prosmotr.Services.Abstractions;

/// <summary>Хранилище запомненных аудиодорожек для папок (сезонов сериалов).
/// Выбор озвучки в одной серии применяется ко всем файлам папки при открытии.</summary>
public interface IFolderAudioTrackStore
{
    /// <summary>Вернуть запомненную дорожку папки или null, если её нет.</summary>
    FolderAudioTrack? Get(string folderPath);

    /// <summary>Запомнить выбранную дорожку для папки (id &gt; 0 — реальная дорожка).</summary>
    void Set(string folderPath, int audioTrackId, string? audioTrackName);

    /// <summary>Сбросить память папки (пользователь выбрал дорожку «По умолчанию»).</summary>
    void Clear(string folderPath);

    /// <summary>Немедленно сбросить на диск (вызывается при выходе из приложения).</summary>
    void Flush();
}
