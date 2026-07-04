using Prosmotr.ViewModels;
using Xunit;

namespace Prosmotr.Tests;

public class FolderSummaryTests
{
    [Theory]
    [InlineData(12, 3, 1, "12 фото, 3 видео, 1 GIF")]
    [InlineData(1, 0, 0, "1 фото")]
    [InlineData(0, 2, 0, "2 видео")]
    [InlineData(0, 0, 5, "5 GIF")]
    [InlineData(7, 1, 0, "7 фото, 1 видео")]
    public void BuildSummaryText_ReturnsExpected(int images, int videos, int gifs, string expected)
    {
        var actual = MainViewModel.BuildFolderSummaryText(images, videos, gifs);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void BuildSummaryText_AllZero_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, MainViewModel.BuildFolderSummaryText(0, 0, 0));
    }
}
