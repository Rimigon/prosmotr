using System.IO;
using Prosmotr.Infrastructure;
using Prosmotr.Models;
using Prosmotr.Services.Abstractions;

namespace Prosmotr.Services;

/// <summary>Сканирует папку, отбирает поддерживаемые медиафайлы и формирует отсортированную галерею.</summary>
public sealed class MediaLibraryService : IMediaLibraryService
{
    public MediaItem? CreateItem(string path)
    {
        var type = SupportedFormats.GetMediaType(path);
        if (type == MediaType.Unknown) return null;
        var item = new MediaItem(path, type);
        TryFillMetadata(item);
        return item;
    }

    public async Task<MediaLibraryResult> BuildFromFileAsync(string filePath, SortSpec sort,
        IReadOnlyList<string>? explicitOrder = null, CancellationToken ct = default)
    {
        var folder = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            return SingleItemResult(filePath);

        var items = await ScanAsync(folder, sort, explicitOrder, ct).ConfigureAwait(false);

        var idx = IndexOfPath(items, filePath);
        if (idx < 0)
        {
            var item = CreateItem(filePath);
            if (item != null)
            {
                var list = items.ToList();
                list.Insert(0, item);
                return new MediaLibraryResult(list, 0);
            }
            idx = 0;
        }

        return new MediaLibraryResult(items, Math.Max(0, idx));
    }

    public async Task<MediaLibraryResult> BuildFromFolderAsync(string folderPath, SortSpec sort,
        IReadOnlyList<string>? explicitOrder = null, CancellationToken ct = default)
    {
        if (!Directory.Exists(folderPath))
            return new MediaLibraryResult(Array.Empty<MediaItem>(), 0);

        var items = await ScanAsync(folderPath, sort, explicitOrder, ct).ConfigureAwait(false);
        return new MediaLibraryResult(items, 0);
    }

    public IReadOnlyList<MediaItem> Sort(IEnumerable<MediaItem> items, SortSpec sort)
    {
        var list = items.ToList();
        StableSort(list, GetComparison(sort));
        return list;
    }

    /// <summary>
    /// Сортировка, устойчивая к нетранзитивному компаратору. <c>StrCmpLogicalW</c>
    /// (натуральная сортировка) на некоторых наборах имён нарушает транзитивность, и
    /// <see cref="List{T}.Sort(Comparison{T})"/> (introsort) детектит это и бросает
    /// <see cref="InvalidOperationException"/>, обрушивая открытие папки. Fallback —
    /// LINQ-сортировка, которая не валидирует компаратор столь жёстко.
    /// </summary>
    private static void StableSort(List<MediaItem> list, Comparison<MediaItem> comparison)
    {
        try
        {
            list.Sort(comparison);
        }
        catch (InvalidOperationException)
        {
            var sorted = list.OrderBy(x => x, Comparer<MediaItem>.Create(comparison)).ToList();
            list.Clear();
            list.AddRange(sorted);
        }
    }

    private static int IndexOfPath(IReadOnlyList<MediaItem> items, string path)
    {
        for (int i = 0; i < items.Count; i++)
            if (string.Equals(items[i].FullPath, path, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    private Task<IReadOnlyList<MediaItem>> ScanAsync(string folder, SortSpec sort,
        IReadOnlyList<string>? explicitOrder, CancellationToken ct) =>
        Task.Run<IReadOnlyList<MediaItem>>(() =>
        {
            var result = new List<MediaItem>();
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.System | FileAttributes.Hidden
            };

            try
            {
                foreach (var path in Directory.EnumerateFiles(folder, "*", options))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var type = SupportedFormats.GetMediaType(path);
                        if (type == MediaType.Unknown) continue;
                        var item = new MediaItem(path, type);
                        TryFillMetadata(item);
                        result.Add(item);
                    }
                    catch { /* файл мог исчезнуть/заблокироваться — пропускаем */ }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { /* нет доступа к папке — игнорируем */ }

            ApplyOrder(result, explicitOrder, sort);
            return result;
        }, ct);

    /// <summary>Упорядочить: по готовому порядку из Проводника либо по полю сортировки.</summary>
    private static void ApplyOrder(List<MediaItem> items, IReadOnlyList<string>? explicitOrder, SortSpec sort)
    {
        if (explicitOrder is { Count: > 0 })
        {
            var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < explicitOrder.Count; i++)
                index[explicitOrder[i]] = i;

            StableSort(items, (a, b) =>
            {
                int ia = index.TryGetValue(a.FullPath, out var x) ? x : int.MaxValue;
                int ib = index.TryGetValue(b.FullPath, out var y) ? y : int.MaxValue;
                return ia != ib ? ia.CompareTo(ib)
                                : NaturalStringComparer.Instance.Compare(a.FileName, b.FileName);
            });
        }
        else
        {
            StableSort(items, GetComparison(sort));
        }
    }

    /// <summary>Компаратор по полю сортировки; вторичный ключ — натуральное имя «как в Проводнике».</summary>
    private static Comparison<MediaItem> GetComparison(SortSpec sort)
    {
        int dir = sort.Descending ? -1 : 1;
        return (a, b) =>
        {
            int cmp = sort.Field switch
            {
                SortField.Size => a.FileSizeBytes.CompareTo(b.FileSizeBytes),
                SortField.DateModified => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc),
                SortField.DateCreated => a.CreationTimeUtc.CompareTo(b.CreationTimeUtc),
                SortField.Type => string.Compare(a.Extension, b.Extension, StringComparison.OrdinalIgnoreCase),
                _ => NaturalStringComparer.Instance.Compare(a.FileName, b.FileName)
            };
            if (cmp == 0 && sort.Field != SortField.Name)
                cmp = NaturalStringComparer.Instance.Compare(a.FileName, b.FileName);
            return dir * cmp;
        };
    }

    private static void TryFillMetadata(MediaItem item)
    {
        try
        {
            var fi = new FileInfo(item.FullPath);
            item.FileSizeBytes = fi.Length;
            item.LastWriteTimeUtc = fi.LastWriteTimeUtc;
            item.CreationTimeUtc = fi.CreationTimeUtc;
        }
        catch { /* файл мог исчезнуть — оставляем нули */ }
    }

    private MediaLibraryResult SingleItemResult(string filePath)
    {
        var item = CreateItem(filePath);
        return item == null
            ? new MediaLibraryResult(Array.Empty<MediaItem>(), 0)
            : new MediaLibraryResult(new[] { item }, 0);
    }
}
