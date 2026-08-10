using Prosmotr.Infrastructure;
using Prosmotr.Models;
using Prosmotr.Services.Abstractions;

namespace Prosmotr.Services;

/// <summary>Список недавних магнет-ссылок поверх AppSettings.RecentMagnets (паттерн RecentFilesService).</summary>
public sealed class RecentMagnetsService : IRecentMagnetsService
{
    private const int MaxItems = 8;
    private readonly ISettingsService _settings;

    public event EventHandler? Changed;

    public RecentMagnetsService(ISettingsService settings) => _settings = settings;

    public IReadOnlyList<RecentMagnetEntry> Items =>
        _settings.Settings.RecentMagnets
            .OrderByDescending(r => r.OpenedAtUtc)
            .ToList();

    public void Add(string magnet, string displayName)
    {
        // Дедуп по infoHash: одна и та же раздача с другими трекерами — не дубликат.
        var hash = MagnetLinkParser.TryGetInfoHash(magnet, out var h) ? h : magnet;
        var updated = _settings.Settings.RecentMagnets
            .Where(r => !(MagnetLinkParser.TryGetInfoHash(r.Magnet, out var rh) && string.Equals(rh, hash, StringComparison.Ordinal)))
            .ToList();
        updated.Insert(0, new RecentMagnetEntry { Magnet = magnet, DisplayName = displayName, OpenedAtUtc = DateTime.UtcNow });
        if (updated.Count > MaxItems)
            updated.RemoveRange(MaxItems, updated.Count - MaxItems);

        // Атомарная подмена ссылки (см. RecentFilesService: мутировать живой список нельзя —
        // дебаунс-таймер SettingsService сериализует Settings на фоне).
        _settings.Settings.RecentMagnets = updated;
        _settings.SaveDebounced();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _settings.Settings.RecentMagnets = new();
        _settings.Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
