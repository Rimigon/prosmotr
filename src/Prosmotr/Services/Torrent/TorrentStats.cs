using System.Globalization;

namespace Prosmotr.Services.Torrent;

/// <summary>
/// Чистые вычисления для UI загрузки (ETA, формат байт, «позиция за границей скачанного») —
/// без зависимости от MonoTorrent, покрыты юнит-тестами. Формат чисел — инвариантный,
/// чтобы UI не зависел от локали (десятичный разделитель).
/// </summary>
public static class TorrentStats
{
    public static long? ComputeEtaSeconds(long remainingBytes, long bytesPerSecond)
    {
        if (bytesPerSecond <= 0) return null;
        if (remainingBytes <= 0) return 0;
        return (remainingBytes + bytesPerSecond - 1) / bytesPerSecond;
    }

    public static string FormatBytes(long bytes)
    {
        const long kb = 1024, mb = 1024 * 1024, gb = 1024L * 1024 * 1024;
        // Явно InvariantCulture: интерполяция с форматом :0.0 сама по себе использует
        // текущую локаль (на русской Windows получили бы «2,0 КБ»).
        string WithUnit(double value, string unit) =>
            value.ToString("0.0", CultureInfo.InvariantCulture) + " " + unit;
        return bytes switch
        {
            >= gb => WithUnit(bytes / (double)gb, "ГБ"),
            >= mb => WithUnit(bytes / (double)mb, "МБ"),
            >= kb => WithUnit(bytes / (double)kb, "КБ"),
            _ => $"{bytes} Б"
        };
    }

    /// <summary>
    /// Позиция воспроизведения дальше, чем скачано (с запасом) → плеер ждёт докачки
    /// (оверлей «Докачивается…»). Запас нужен, чтобы не мигать на границе.
    /// </summary>
    public static bool IsBeyondDownloaded(long positionMs, long lengthMs, double downloadedPercent, long slackMs)
    {
        if (lengthMs <= 0) return false;
        var downloadedMs = lengthMs * (downloadedPercent / 100.0);
        return positionMs > downloadedMs + slackMs;
    }
}
