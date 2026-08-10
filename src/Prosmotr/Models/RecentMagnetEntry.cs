namespace Prosmotr.Models;

/// <summary>Запись в списке недавних магнет-ссылок (стартовый экран).</summary>
public sealed class RecentMagnetEntry
{
    public string Magnet { get; set; } = string.Empty;
    /// <summary>Отображаемое имя — из &amp;dn= ссылки или префикс infoHash.</summary>
    public string DisplayName { get; set; } = string.Empty;
    public DateTime OpenedAtUtc { get; set; }
}
