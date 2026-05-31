namespace Prosmotr.Models;

/// <summary>Сохранённая позиция и (опционально) скорость воспроизведения для конкретного видео.</summary>
public sealed class PlaybackPosition
{
    public long PositionMs { get; set; }
    public long DurationMs { get; set; }
    public float? Rate { get; set; }
}
