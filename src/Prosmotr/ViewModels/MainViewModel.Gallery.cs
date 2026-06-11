using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.Input;
using Prosmotr.Infrastructure;
using Prosmotr.Models;
using Prosmotr.Services.Abstractions;

namespace Prosmotr.ViewModels;

/// <summary>Часть MainViewModel: галерея, открытие папок/файлов, Drag-and-Drop, сортировка.</summary>
public sealed partial class MainViewModel
{
    private CancellationTokenSource? _openCts;
    private bool _suppressSortChange;
    private string? _currentFolderKey;

    // --- Открытие ---

    [RelayCommand]
    private async Task OpenFile()
    {
        var path = _dialog.OpenFile(SupportedFormats.AllExtensions);
        if (path != null) await OpenPathAsync(path);
    }

    [RelayCommand]
    private async Task OpenFolder()
    {
        var path = _dialog.OpenFolder();
        if (path != null) await OpenPathAsync(path);
    }

    /// <summary>Открыть путь (файл или папку), построить галерею и перейти к нему.</summary>
    public async Task OpenPathAsync(string path)
    {
        var sw = Stopwatch.StartNew();
        _openCts?.Cancel();
        _openCts?.Dispose();
        _openCts = new CancellationTokenSource();
        var ct = _openCts.Token;

        try
        {
            MediaLibraryResult result;
            if (Directory.Exists(path))
            {
                var (sort, order) = await ResolveOrderingAsync(path);
                result = await _library.BuildFromFolderAsync(path, sort, order, ct);
                _recent.Add(path, isFolder: true);
                if (order == null) ReflectSort(sort);
            }
            else if (File.Exists(path) && SupportedFormats.IsSupported(path))
            {
                var folder = Path.GetDirectoryName(path) ?? string.Empty;
                var (sort, order) = await ResolveOrderingAsync(folder);
                result = await _library.BuildFromFileAsync(path, sort, order, ct);
                _recent.Add(path, isFolder: false);
                _settings.Settings.LastFilePath = path;
                _settings.SaveDebounced();
                if (order == null) ReflectSort(sort);
            }
            else
            {
                _notify.Show("Формат файла не поддерживается.", NotificationKind.Warning);
                return;
            }

            if (result.Items.Count == 0)
            {
                _nav.Clear();
                _notify.Show("В папке нет поддерживаемых медиафайлов.", NotificationKind.Warning);
                return;
            }

            ClearUndoState(); // открыли другую галерею — отмена прежнего удаления неактуальна
            _nav.SetItems(result.Items, result.StartIndex);
            AppLog.Write($"[Perf] OpenPathAsync total ({Path.GetFileName(path)}): {sw.ElapsedMilliseconds} ms");
        }
        catch (Exception ex)
        {
            AppLog.Error("OpenPathAsync", ex);
            _notify.Show("Не удалось открыть файл или папку.", NotificationKind.Error);
        }
    }

    public Task HandleDropAsync(IEnumerable<string> paths)
    {
        var path = paths.FirstOrDefault(p => File.Exists(p) || Directory.Exists(p));
        return path != null ? OpenPathAsync(path) : Task.CompletedTask;
    }

    // --- Сортировка ---

    /// <summary>
    /// Определить порядок галереи. Приоритет: ручной выбор пользователя для папки →
    /// реальный порядок открытого окна Проводника → глобальная настройка.
    /// Возвращает либо поле сортировки (sort), либо готовый порядок путей (order).
    /// </summary>
    private async Task<(SortSpec sort, IReadOnlyList<string>? order)> ResolveOrderingAsync(string folder)
    {
        var sw = Stopwatch.StartNew();
        var key = string.IsNullOrEmpty(folder) ? null : folder.ToLowerInvariant().TrimEnd('\\', '/');
        _currentFolderKey = key;

        // 1) Явный выбор пользователя для этой папки — высший приоритет.
        if (key != null && _settings.Settings.ManualFolderSorts.TryGetValue(key, out var manual) &&
            TryParseSpec(manual, out var manualSpec))
        {
            AppLog.Write($"[Perf] Sort resolve (manual): {sw.ElapsedMilliseconds} ms");
            return (manualSpec, null);
        }

        // 2) Реальный порядок открытого окна Проводника — повторяет ЛЮБУЮ сортировку Windows.
        if (_settings.Settings.MatchExplorerSort && !string.IsNullOrEmpty(folder))
        {
            var explorerTask = Task.Run(() =>
            {
                try
                {
                    ExplorerSortReader.TryGetOrderedPaths(folder, out var list);
                    return list;
                }
                catch (Exception ex)
                {
                    AppLog.Error("ResolveOrderingAsync explorer sort", ex);
                    return (List<string>?)null;
                }
            });
            var ordered = await Task.WhenAny(explorerTask, Task.Delay(3000)) == explorerTask
                ? await explorerTask
                : null;
            if (ordered is { Count: > 0 })
            {
                AppLog.Write($"[Perf] Sort resolve (explorer, {ordered.Count} items): {sw.ElapsedMilliseconds} ms");
                return (default, ordered);
            }
            AppLog.Write($"[Perf] Sort resolve (explorer timeout/fallback): {sw.ElapsedMilliseconds} ms");
        }

        // 3) Глобальная настройка по умолчанию.
        AppLog.Write($"[Perf] Sort resolve (settings): {sw.ElapsedMilliseconds} ms");
        return (new SortSpec(_settings.Settings.SortBy, _settings.Settings.SortDescending), null);
    }

    private void ReflectSort(SortSpec sort)
    {
        _suppressSortChange = true;
        SelectedSortField = sort.Field;
        SortDescending = sort.Descending;
        _suppressSortChange = false;
    }

    partial void OnSelectedSortFieldChanged(SortField value)
    {
        if (!_suppressSortChange) PersistAndApplySort();
    }

    partial void OnSortDescendingChanged(bool value)
    {
        if (!_suppressSortChange) PersistAndApplySort();
    }

    [RelayCommand]
    private void ToggleSortDirection() => SortDescending = !SortDescending;

    private void PersistAndApplySort()
    {
        _settings.Settings.SortBy = SelectedSortField;
        _settings.Settings.SortDescending = SortDescending;
        // Запоминаем выбор пользователя для текущей папки — он перекрывает Проводник при след. открытии.
        if (_currentFolderKey != null)
            _settings.Settings.ManualFolderSorts[_currentFolderKey] = $"{SelectedSortField}:{SortDescending}";
        _settings.SaveDebounced();
        ApplySort();
    }

    private static bool TryParseSpec(string value, out SortSpec spec)
    {
        spec = default;
        var parts = value.Split(':');
        if (parts.Length == 2 && Enum.TryParse<SortField>(parts[0], out var f) && bool.TryParse(parts[1], out var d))
        {
            spec = new SortSpec(f, d);
            return true;
        }
        return false;
    }

    private void ApplySort()
    {
        if (!_nav.HasItems) return;
        var sorted = _library.Sort(_nav.Items, new SortSpec(SelectedSortField, SortDescending));
        _nav.ReorderPreservingCurrent(sorted);
    }
}
