using System.IO;

namespace Prosmotr.Tests;

/// <summary>Уникальная временная папка, удаляемая по Dispose — для тестов файлового хранилища.</summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ProsmotrTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { /* временная папка — не критично */ }
    }
}
