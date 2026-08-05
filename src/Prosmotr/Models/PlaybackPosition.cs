namespace Prosmotr.Models;

/// <summary>Сохранённая позиция, скорость и аудиодорожка для конкретного видео.</summary>
public sealed class PlaybackPosition
{
    public long PositionMs { get; set; }
    public long DurationMs { get; set; }
    public float? Rate { get; set; }
    /// <summary>Выбранная аудиодорожка (id из TrackDescription) — для восстановления при следующем открытии.</summary>
    public int? AudioTrackId { get; set; }
    /// <summary>Имя дорожки — запасной вариант сопоставления, если id сместился (файл пересобран).</summary>
    public string? AudioTrackName { get; set; }
}
