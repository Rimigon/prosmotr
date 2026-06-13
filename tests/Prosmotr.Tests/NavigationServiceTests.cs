using Prosmotr.Models;
using Prosmotr.Services;
using Xunit;

namespace Prosmotr.Tests;

/// <summary>Тесты навигации: индексы при удалении/вставке и циклический переход.</summary>
public sealed class NavigationServiceTests
{
    private static MediaItem Item(string name) =>
        new($@"C:\photos\{name}", MediaType.Image);

    private static NavigationService WithItems(int startIndex, params string[] names)
    {
        var nav = new NavigationService();
        nav.SetItems(names.Select(Item).ToList(), startIndex);
        return nav;
    }

    [Fact]
    public void SetItems_ClampsStartIndex()
    {
        var nav = WithItems(99, "a.jpg", "b.jpg");
        Assert.Equal(1, nav.CurrentIndex);
        Assert.Equal("b.jpg", nav.Current!.FileName);
    }

    [Fact]
    public void SetItems_Empty_SetsIndexToMinusOne()
    {
        var nav = WithItems(0);
        Assert.Equal(-1, nav.CurrentIndex);
        Assert.Null(nav.Current);
        Assert.False(nav.HasItems);
    }

    [Fact]
    public void RemoveAt_BeforeCurrent_ShiftsCurrentLeft()
    {
        var nav = WithItems(2, "a.jpg", "b.jpg", "c.jpg");
        nav.RemoveAt(0); // удаляем "a", текущий был "c"
        Assert.Equal(1, nav.CurrentIndex);
        Assert.Equal("c.jpg", nav.Current!.FileName);
    }

    [Fact]
    public void RemoveAt_AfterCurrent_KeepsCurrent()
    {
        var nav = WithItems(0, "a.jpg", "b.jpg", "c.jpg");
        nav.RemoveAt(2); // удаляем "c", текущий "a"
        Assert.Equal(0, nav.CurrentIndex);
        Assert.Equal("a.jpg", nav.Current!.FileName);
    }

    [Fact]
    public void RemoveAt_Current_ShowsNextAtSameIndex()
    {
        var nav = WithItems(1, "a.jpg", "b.jpg", "c.jpg");
        nav.RemoveAt(1); // удаляем текущий "b" → должен показаться "c" на том же индексе 1
        Assert.Equal(1, nav.CurrentIndex);
        Assert.Equal("c.jpg", nav.Current!.FileName);
    }

    [Fact]
    public void RemoveAt_CurrentLast_ShowsPrevious()
    {
        var nav = WithItems(2, "a.jpg", "b.jpg", "c.jpg");
        nav.RemoveAt(2); // удаляем последний текущий → показать предыдущий
        Assert.Equal(1, nav.CurrentIndex);
        Assert.Equal("b.jpg", nav.Current!.FileName);
    }

    [Fact]
    public void RemoveAt_LastRemaining_GoesEmpty()
    {
        var nav = WithItems(0, "a.jpg");
        nav.RemoveAt(0);
        Assert.Equal(-1, nav.CurrentIndex);
        Assert.Null(nav.Current);
    }

    [Fact]
    public void MoveNext_IsCyclic()
    {
        var nav = WithItems(1, "a.jpg", "b.jpg");
        nav.MoveNext(); // 1 → 0 (по кругу)
        Assert.Equal(0, nav.CurrentIndex);
    }

    [Fact]
    public void MovePrevious_IsCyclic()
    {
        var nav = WithItems(0, "a.jpg", "b.jpg");
        nav.MovePrevious(); // 0 → 1 (по кругу)
        Assert.Equal(1, nav.CurrentIndex);
    }

    [Fact]
    public void InsertAt_RestoresAtIndexAndBecomesCurrent()
    {
        var nav = WithItems(0, "a.jpg", "c.jpg");
        nav.InsertAt(Item("b.jpg"), 1);
        Assert.Equal(1, nav.CurrentIndex);
        Assert.Equal("b.jpg", nav.Current!.FileName);
        Assert.Equal(3, nav.Items.Count);
    }

    [Fact]
    public void InsertAt_DuplicatePath_MovesToExistingInsteadOfDuplicating()
    {
        var nav = WithItems(0, "a.jpg", "b.jpg");
        nav.InsertAt(Item("b.jpg"), 0); // тот же путь уже есть
        Assert.Equal(2, nav.Items.Count); // без дубля
        Assert.Equal("b.jpg", nav.Current!.FileName);
    }

    [Fact]
    public void InsertAt_ClampsOutOfRangeIndex()
    {
        var nav = WithItems(0, "a.jpg");
        nav.InsertAt(Item("z.jpg"), 999);
        Assert.Equal("z.jpg", nav.Current!.FileName);
        Assert.Equal(2, nav.Items.Count);
    }

    [Fact]
    public void MoveTo_ByItemPath_IsCaseInsensitive()
    {
        var nav = WithItems(0, "a.jpg", "b.jpg");
        nav.MoveTo(new MediaItem(@"C:\photos\B.JPG", MediaType.Image));
        Assert.Equal(1, nav.CurrentIndex);
    }

    [Fact]
    public void ReorderPreservingCurrent_KeepsCurrentFile()
    {
        var nav = WithItems(0, "a.jpg", "b.jpg", "c.jpg");
        var reordered = new[] { Item("c.jpg"), Item("b.jpg"), Item("a.jpg") };
        nav.ReorderPreservingCurrent(reordered);
        Assert.Equal("a.jpg", nav.Current!.FileName); // тот же файл, новый индекс
        Assert.Equal(2, nav.CurrentIndex);
    }
}
