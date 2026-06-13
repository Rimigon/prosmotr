using Prosmotr.Infrastructure;
using Xunit;

namespace Prosmotr.Tests;

/// <summary>Построение file-URI из путей со спецсимволами (# и %).</summary>
public sealed class PathUriTests
{
    [Fact]
    public void Escape_EscapesPercentThenHash_InCorrectOrder()
    {
        // Сначала % → %25, затем # → %23. Порядок важен: иначе уже вставленные %25 задвоятся.
        Assert.Equal("a%25b%23c", PathUri.Escape("a%b#c"));
    }

    [Fact]
    public void ToUri_PlainPath_BuildsFileUri()
    {
        var uri = PathUri.ToUri(@"C:\photos\image.jpg");
        Assert.True(uri.IsAbsoluteUri);
        Assert.Equal(Uri.UriSchemeFile, uri.Scheme);
    }

    [Fact]
    public void ToUri_PathWithHash_DoesNotThrowAndEscapes()
    {
        var uri = PathUri.ToUri(@"C:\photos\a#b.gif");
        Assert.True(uri.IsAbsoluteUri);
        Assert.Contains("%23", uri.AbsoluteUri); // # экранирован, не воспринят как фрагмент
        Assert.True(string.IsNullOrEmpty(uri.Fragment));
    }

    [Fact]
    public void ToUri_PathWithPercent_DoesNotThrow()
    {
        var ex = Record.Exception(() => PathUri.ToUri(@"C:\photos\100%done.webp"));
        Assert.Null(ex);
    }
}
