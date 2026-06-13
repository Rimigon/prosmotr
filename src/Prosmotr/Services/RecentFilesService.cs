using Prosmotr.Models;
using Prosmotr.Services.Abstractions;

namespace Prosmotr.Services;

/// <summary>Список недавних файлов и папок поверх AppSettings.RecentFiles.</summary>
public sealed class RecentFilesService : IRecentFilesService
{
    private const int MaxItems = 15;
    private readonly ISettingsService _settings;

    public event EventHandler? Changed;

    public RecentFilesService(ISettingsService settings) => _settings = settings;

    public IReadOnlyList<RecentEntry> Items =>
        _settings.Settings.RecentFiles
            .OrderByDescending(r => r.OpenedAtUtc)
            .ToList();

    public void Add(string path, bool isFolder)
    {
        // Собираем новый список и подменяем ссылку атомарно. Мутировать живой список нельзя:
        // дебаунс-таймер SettingsService сериализует Settings на фоновом потоке, и
        // одновременное изменение коллекции бросило бы исключение внутри JsonSerializer
        // (настройки молча не сохранились бы).
        var updated = _settings.Settings.RecentFiles
            .Where(r => !string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase))
            .ToList();
        updated.Insert(0, new RecentEntry { Path = path, IsFolder = isFolder, OpenedAtUtc = DateTime.UtcNow });
        if (updated.Count > MaxItems)
            updated.RemoveRange(MaxItems, updated.Count - MaxItems);

        _settings.Settings.RecentFiles = updated;
        _settings.SaveDebounced();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _settings.Settings.RecentFiles = new();
        _settings.Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
