using System.IO;
using Prosmotr.Models;
using Prosmotr.Services;
using Xunit;

namespace Prosmotr.Tests;

/// <summary>Интеграционные тесты сканирования папки на реальных файлах
/// (покрывают перечисление через DirectoryInfo, заполнение метаданных и сортировку).</summary>
public sealed class MediaLibraryScanTests
{
    private readonly MediaLibraryService _svc = new();

    private static void Write(TempDir dir, string name, string content = "x")
        => File.WriteAllText(dir.File(name), content);

    [Fact]
    public async Task BuildFromFolder_ReturnsOnlySupported_SortedByName()
    {
        using var dir = new TempDir();
        Write(dir, "p10.jpg");
        Write(dir, "p2.jpg");
        Write(dir, "p1.jpg");
        Write(dir, "notes.txt");   // не медиа — должен быть исключён
        Write(dir, "clip.mp4");

        var result = await _svc.BuildFromFolderAsync(dir.Path, new SortSpec(SortField.Name, false));

        var names = result.Items.Select(i => i.FileName).ToArray();
        Assert.DoesNotContain("notes.txt", names);
        // натуральная сортировка: p1, p2, p10, затем clip (по имени 'c' < 'p')
        Assert.Equal(new[] { "clip.mp4", "p1.jpg", "p2.jpg", "p10.jpg" }, names);
    }

    [Fact]
    public async Task BuildFromFolder_PopulatesMetadataFromDirectoryEntry()
    {
        using var dir = new TempDir();
        Write(dir, "a.jpg", "содержимое подлиннее для ненулевого размера");

        var result = await _svc.BuildFromFolderAsync(dir.Path, new SortSpec(SortField.Name, false));

        var item = Assert.Single(result.Items);
        Assert.True(item.FileSizeBytes > 0);                 // размер заполнен из FileInfo записи каталога
        Assert.True(item.LastWriteTimeUtc > DateTime.MinValue);
    }

    [Fact]
    public async Task BuildFromFolder_SortBySize_OrdersAscending()
    {
        using var dir = new TempDir();
        Write(dir, "big.jpg", new string('x', 5000));
        Write(dir, "small.jpg", "x");
        Write(dir, "mid.jpg", new string('x', 1000));

        var result = await _svc.BuildFromFolderAsync(dir.Path, new SortSpec(SortField.Size, false));

        Assert.Equal(new[] { "small.jpg", "mid.jpg", "big.jpg" }, result.Items.Select(i => i.FileName));
    }

    [Fact]
    public async Task BuildFromFolder_EmptyOrMissing_ReturnsEmpty()
    {
        using var dir = new TempDir();
        var result = await _svc.BuildFromFolderAsync(dir.Path, new SortSpec(SortField.Name, false));
        Assert.Empty(result.Items);

        var missing = await _svc.BuildFromFolderAsync(Path.Combine(dir.Path, "nope"), new SortSpec(SortField.Name, false));
        Assert.Empty(missing.Items);
    }
}
