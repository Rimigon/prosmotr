using Prosmotr.Infrastructure;
using Xunit;

namespace Prosmotr.Tests;

/// <summary>Натуральная сортировка имён «как в Проводнике».</summary>
public sealed class NaturalStringComparerTests
{
    [Fact]
    public void File2_SortsBefore_File10()
    {
        Assert.True(NaturalStringComparer.Instance.Compare("file2.jpg", "file10.jpg") < 0);
    }

    [Fact]
    public void Sort_OrdersNumericallyNotLexicographically()
    {
        var names = new List<string> { "img10.jpg", "img2.jpg", "img1.jpg", "img100.jpg" };
        names.Sort(NaturalStringComparer.Instance);
        Assert.Equal(new[] { "img1.jpg", "img2.jpg", "img10.jpg", "img100.jpg" }, names);
    }

    [Fact]
    public void Compare_HandlesNullsAsEmpty()
    {
        // Не должно бросать NRE — внутри подставляется string.Empty.
        var ex = Record.Exception(() => NaturalStringComparer.Instance.Compare(null, "a"));
        Assert.Null(ex);
    }
}
