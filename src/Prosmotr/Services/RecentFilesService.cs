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
        var list = _settings.Settings.RecentFiles;
        list.RemoveAll(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, new RecentEntry { Path = path, IsFolder = isFolder, OpenedAtUtc = DateTime.UtcNow });
        if (list.Count > MaxItems)
            list.RemoveRange(MaxItems, list.Count - MaxItems);

        _settings.SaveDebounced();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _settings.Settings.RecentFiles.Clear();
        _settings.Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
