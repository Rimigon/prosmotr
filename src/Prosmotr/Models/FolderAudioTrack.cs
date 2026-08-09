namespace Prosmotr.Models;

/// <summary>Запомненная аудиодорожка для ПАПКИ (сезона сериала). Применяется ко всем видео
/// папки, у которых такая дорожка есть. id — как запасной ключ, имя — основной способ
/// сопоставления (в разных сериях дорожки могут идти в разном порядке).</summary>
public sealed class FolderAudioTrack
{
    /// <summary>Выбранная аудиодорожка (id из TrackDescription) для папки.</summary>
    public int? AudioTrackId { get; set; }

    /// <summary>Имя дорожки — запасной вариант сопоставления, если id сместился.</summary>
    public string? AudioTrackName { get; set; }
}
