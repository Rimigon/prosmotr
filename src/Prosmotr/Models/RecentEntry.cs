namespace Prosmotr.Models;

/// <summary>Запись в списке недавних файлов/папок.</summary>
public sealed class RecentEntry
{
    public string Path { get; set; } = string.Empty;
    public bool IsFolder { get; set; }
    public DateTime OpenedAtUtc { get; set; }
}
