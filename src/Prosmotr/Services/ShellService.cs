using System.Diagnostics;
using System.IO;
using System.Windows;
using Prosmotr.Services.Abstractions;

namespace Prosmotr.Services;

/// <summary>Действия с оболочкой Windows: проводник, буфер обмена, свойства файла, открытие URI.</summary>
public sealed class ShellService : IShellService
{
    public void ShowInExplorer(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return;
        try
        {
            var safe = path.Replace("\"", "\\\"");
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{safe}\"") { UseShellExecute = true });
        }
        catch { /* проводник недоступен — игнорируем */ }
    }

    public void OpenContainingFolder(string path)
    {
        var folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(folder)) return;
        try
        {
            var safe = folder.Replace("\"", "\\\"");
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{safe}\"") { UseShellExecute = true });
        }
        catch { }
    }

    public void CopyPathToClipboard(string path)
    {
        try { Clipboard.SetText(path); } catch { }
    }

    public void OpenWith(string path)
    {
        if (!File.Exists(path)) return;
        try
        {
            var safe = path.Replace("\"", "\\\"");
            // Системный диалог «Открыть с помощью…» (надёжно работает через rundll32).
            // Путь обязательно в кавычках — иначе пробелы разбивают его на аргументы.
            Process.Start(new ProcessStartInfo("rundll32.exe",
                $"shell32.dll,OpenAs_RunDLL \"{safe}\"") { UseShellExecute = true });
        }
        catch { }
    }

    public void OpenUri(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch { }
    }
}
