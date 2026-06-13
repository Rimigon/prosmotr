using Prosmotr.Models;
using Prosmotr.Services;
using Xunit;

namespace Prosmotr.Tests;

/// <summary>Сортировка галереи по полям и устойчивость к большому набору имён.</summary>
public sealed class MediaLibrarySortTests
{
    private static MediaItem Item(string name, long size = 0, DateTime modified = default)
    {
        var it = new MediaItem($@"C:\g\{name}", MediaType.Image)
        {
            FileSizeBytes = size,
            LastWriteTimeUtc = modified
        };
        return it;
    }

    private readonly MediaLibraryService _svc = new();

    [Fact]
    public void Sort_ByName_IsNatural()
    {
        var items = new[] { Item("p10.jpg"), Item("p2.jpg"), Item("p1.jpg") };
        var sorted = _svc.Sort(items, new SortSpec(SortField.Name, false));
        Assert.Equal(new[] { "p1.jpg", "p2.jpg", "p10.jpg" }, sorted.Select(i => i.FileName));
    }

    [Fact]
    public void Sort_ByName_Descending_ReversesOrder()
    {
        var items = new[] { Item("p1.jpg"), Item("p2.jpg"), Item("p10.jpg") };
        var sorted = _svc.Sort(items, new SortSpec(SortField.Name, true));
        Assert.Equal(new[] { "p10.jpg", "p2.jpg", "p1.jpg" }, sorted.Select(i => i.FileName));
    }

    [Fact]
    public void Sort_BySize_OrdersAscending()
    {
        var items = new[] { Item("a.jpg", 300), Item("b.jpg", 100), Item("c.jpg", 200) };
        var sorted = _svc.Sort(items, new SortSpec(SortField.Size, false));
        Assert.Equal(new[] { "b.jpg", "c.jpg", "a.jpg" }, sorted.Select(i => i.FileName));
    }

    [Fact]
    public void Sort_ByDateModified_OrdersAscending()
    {
        var items = new[]
        {
            Item("a.jpg", modified: new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc)),
            Item("b.jpg", modified: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            Item("c.jpg", modified: new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc)),
        };
        var sorted = _svc.Sort(items, new SortSpec(SortField.DateModified, false));
        Assert.Equal(new[] { "b.jpg", "c.jpg", "a.jpg" }, sorted.Select(i => i.FileName));
    }

    [Fact]
    public void Sort_LargeMixedNameSet_DoesNotThrow()
    {
        // Регрессионный тест на устойчивость StableSort: натуральный компаратор
        // (StrCmpLogicalW) на больших наборах со смесью цифр/символов может нарушать
        // транзитивность; List.Sort это детектит и бросает — StableSort должен это пережить.
        var rnd = new Random(12345);
        var samples = new[] { "file", "img", "DSC", "2024-01", "α", "_tmp", "100%", "v1.2" };
        var items = Enumerable.Range(0, 2000)
            .Select(i => Item($"{samples[rnd.Next(samples.Length)]}{rnd.Next(1000)}_{i}.jpg"))
            .ToArray();

        var ex = Record.Exception(() => _svc.Sort(items, new SortSpec(SortField.Name, false)));
        Assert.Null(ex);
    }
}
