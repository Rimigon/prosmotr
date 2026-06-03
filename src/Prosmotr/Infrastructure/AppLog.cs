using System.IO;

namespace Prosmotr.Infrastructure;

/// <summary>Простое файловое логирование диагностики в %LOCALAPPDATA%\Prosmotr\app.log.</summary>
public static class AppLog
{
    private static readonly string LogFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Prosmotr", "app.log");

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);

            var fi = new FileInfo(LogFile);
            if (fi.Exists && fi.Length > 10 * 1024 * 1024)
            {
                var old = LogFile + ".old";
                if (File.Exists(old)) File.Delete(old);
                File.Move(LogFile, old);
            }

            File.AppendAllText(LogFile, $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { /* логирование не должно влиять на работу */ }
    }

    public static void Error(string context, Exception ex) => Write($"ERROR {context}: {ex}");
}
