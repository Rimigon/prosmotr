namespace Prosmotr.Models;

/// <summary>
/// Статус торрент-сессии. Порядок отражает жизненный цикл:
/// ResolvingMetadata (ждём метаданные от пиров) → Downloading → ReadyToPlay
/// (поток создан, можно запускать плеер) → Playing. Error/Stopped — терминальные.
/// </summary>
public enum TorrentStatus
{
    ResolvingMetadata,
    Downloading,
    ReadyToPlay,
    Playing,
    Stopped,
    Error
}
