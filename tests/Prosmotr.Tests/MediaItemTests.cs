using Prosmotr.Models;
using Xunit;

namespace Prosmotr.Tests;

/// <summary>Производные свойства MediaItem (имя, расширение, классификация типа).</summary>
public sealed class MediaItemTests
{
    [Fact]
    public void DerivesNameExtensionAndDirectory()
    {
        var item = new MediaItem(@"C:\photos\sub\image.JPG", MediaType.Image);
        Assert.Equal("image.JPG", item.FileName);
        Assert.Equal(".JPG", item.Extension);
        Assert.Equal(@"C:\photos\sub", item.DirectoryPath);
    }

    [Theory]
    [InlineData(MediaType.Image, true, false, false)]
    [InlineData(MediaType.AnimatedImage, true, false, true)]
    [InlineData(MediaType.Video, false, true, false)]
    public void ClassifiesMediaType(MediaType type, bool isImage, bool isVideo, bool isAnimated)
    {
        var item = new MediaItem(@"C:\x\f", type);
        Assert.Equal(isImage, item.IsImage);
        Assert.Equal(isVideo, item.IsVideo);
        Assert.Equal(isAnimated, item.IsAnimated);
    }

    [Fact]
    public void AnimatedImage_CountsAsImageButNotVideo()
    {
        var gif = new MediaItem(@"C:\x\a.gif", MediaType.AnimatedImage);
        Assert.True(gif.IsImage);   // анимация — частный случай изображения
        Assert.True(gif.IsAnimated);
        Assert.False(gif.IsVideo);
    }
}
