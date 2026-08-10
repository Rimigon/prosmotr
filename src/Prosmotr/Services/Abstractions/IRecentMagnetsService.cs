using Prosmotr.Models;

namespace Prosmotr.Services.Abstractions;

/// <summary>Список недавних магнет-ссылок поверх AppSettings.RecentMagnets.</summary>
public interface IRecentMagnetsService
{
    event EventHandler? Changed;
    IReadOnlyList<RecentMagnetEntry> Items { get; }

    /// <summary>Добавить ссылку (дедуп по infoHash — один торрент = одна запись, даже с разными трекерами).</summary>
    void Add(string magnet, string displayName);

    void Clear();
}
