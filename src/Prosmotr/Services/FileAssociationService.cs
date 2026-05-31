using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Prosmotr.Infrastructure;
using Prosmotr.Services.Abstractions;

namespace Prosmotr.Services;

/// <summary>
/// Регистрация ассоциаций файлов в HKCU (без прав администратора): ProgID, пункт «Открыть с помощью»,
/// контекстное меню. Сделать приложение «по умолчанию» программно в Windows 11 нельзя — система
/// защищает выбор (UCPD/UserChoiceLatest), поэтому открываем «Приложения по умолчанию».
/// </summary>
public sealed class FileAssociationService : IFileAssociationService
{
    private const string ProgId = "Prosmotr.Media";
    private const string AppName = "Просмотр";

    private static string ExePath =>
        Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName!;

    public bool IsRegistered
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ProgId}");
            return key != null;
        }
    }

    public void Register()
    {
        var exe = ExePath;

        // 1. ProgID с иконкой и командой запуска
        using (var progId = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
        {
            progId.SetValue("", "Медиафайл (Просмотр)");
            using (var icon = progId.CreateSubKey("DefaultIcon"))
                icon.SetValue("", $"\"{exe}\",0");
            using (var cmd = progId.CreateSubKey(@"shell\open\command"))
                cmd.SetValue("", $"\"{exe}\" \"%1\"");
        }

        // 2. Пункт «Открыть с помощью → Просмотр» + пункт контекстного меню для расширений
        foreach (var ext in SupportedFormats.AllExtensions)
        {
            using (var extKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ext}\OpenWithProgids"))
                extKey.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);

            using var verb = Registry.CurrentUser.CreateSubKey(
                $@"Software\Classes\SystemFileAssociations\{ext}\shell\Prosmotr.Open");
            verb.SetValue("", "Открыть в «Просмотр»");
            verb.SetValue("Icon", $"\"{exe}\",0");
            using var cmd2 = verb.CreateSubKey("command");
            cmd2.SetValue("", $"\"{exe}\" \"%1\"");
        }

        // 3. Capabilities + RegisteredApplications — чтобы приложение появилось в «Приложениях по умолчанию»
        using (var caps = Registry.CurrentUser.CreateSubKey(@"Software\Prosmotr\Capabilities"))
        {
            caps.SetValue("ApplicationName", AppName);
            caps.SetValue("ApplicationDescription", "Современный просмотрщик фото и видео");
            using var assoc = caps.CreateSubKey("FileAssociations");
            foreach (var ext in SupportedFormats.AllExtensions)
                assoc.SetValue(ext, ProgId);
        }
        using (var regApps = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
            regApps.SetValue(AppName, @"Software\Prosmotr\Capabilities");

        NotifyShell();
    }

    public void Unregister()
    {
        TryDelete(() => Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ProgId}", false));

        foreach (var ext in SupportedFormats.AllExtensions)
        {
            TryDelete(() =>
            {
                using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ext}\OpenWithProgids", true);
                key?.DeleteValue(ProgId, false);
            });
            TryDelete(() => Registry.CurrentUser.DeleteSubKeyTree(
                $@"Software\Classes\SystemFileAssociations\{ext}\shell\Prosmotr.Open", false));
        }

        TryDelete(() => Registry.CurrentUser.DeleteSubKeyTree(@"Software\Prosmotr", false));
        TryDelete(() =>
        {
            using var regApps = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", true);
            regApps?.DeleteValue(AppName, false);
        });

        NotifyShell();
    }

    public void OpenDefaultAppsSettings() =>
        Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });

    private static void TryDelete(Action action)
    {
        try { action(); } catch { /* ключа может не быть */ }
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    private static void NotifyShell() =>
        SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, IntPtr.Zero, IntPtr.Zero);
}
