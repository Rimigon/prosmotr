using Prosmotr.Infrastructure;
using Prosmotr.Models;
using Xunit;

namespace Prosmotr.Tests;

/// <summary>Тесты определения типа медиафайла по расширению.</summary>
public sealed class SupportedFormatsTests
{
    [Theory]
    [InlineData(@"C:\x\photo.jpg", MediaType.Image)]
    [InlineData(@"C:\x\photo.JPG", MediaType.Image)]       // регистронезависимо
    [InlineData(@"C:\x\photo.webp", MediaType.Image)]
    [InlineData(@"C:\x\photo.heic", MediaType.Image)]
    [InlineData(@"C:\x\anim.gif", MediaType.AnimatedImage)]
    [InlineData(@"C:\x\clip.mp4", MediaType.Video)]
    [InlineData(@"C:\x\clip.MKV", MediaType.Video)]
    [InlineData(@"C:\x\doc.txt", MediaType.Unknown)]
    [InlineData(@"C:\x\noext", MediaType.Unknown)]
    public void GetMediaType_ClassifiesByExtension(string path, MediaType expected)
    {
        Assert.Equal(expected, SupportedFormats.GetMediaType(path));
    }

    [Fact]
    public void IsSupported_TrueForKnown_FalseForUnknown()
    {
        Assert.True(SupportedFormats.IsSupported(@"C:\x\a.png"));
        Assert.False(SupportedFormats.IsSupported(@"C:\x\a.exe"));
    }

    [Fact]
    public void RequiresMagick_OnlyForWebpHeicHeif()
    {
        Assert.True(SupportedFormats.RequiresMagick(@"C:\x\a.webp"));
        Assert.True(SupportedFormats.RequiresMagick(@"C:\x\a.HEIC"));
        Assert.False(SupportedFormats.RequiresMagick(@"C:\x\a.jpg"));
    }

    [Fact]
    public void AllExtensions_HasNoDuplicates_AndCoversCategories()
    {
        var all = SupportedFormats.AllExtensions.ToList();
        var distinct = all.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(distinct.Count, all.Count); // нет дубликатов даже без учёта регистра
        Assert.Contains(".jpg", all);
        Assert.Contains(".gif", all);
        Assert.Contains(".mp4", all);
    }
}
