using System.Diagnostics;
using Microsoft.Win32;

namespace Prosmotr.Services.Torrent;

/// <summary>
/// Регистрация magnet: протокола в HKCU — клик по магнет-ссылке в браузере открывает «Просмотр».
///
/// Четыре части (БЕЗ Capabilities\UrlAssociations Windows 11 НЕ показывает приложение
/// в «Приложениях по умолчанию» для magnet: — видны только qBittorrent и т.п.):
///   1) ProgID «Prosmotr.Magnet» (URL Protocol) — то, что выбирает пользователь;
///   2) magnet → ProgID (+ прямая команда как запасная);
///   3) Software\Prosmotr\Capabilities\UrlAssociations\magnet = ProgID;
///   4) Software\RegisteredApplications\«Просмотр» → Capabilities.
///
/// Включается тумблером в настройках (по умолчанию выключен: не перехватываем ссылки
/// у других клиентов без явного согласия). Unregister убирает только magnet-специфичное
/// и НЕ трогает FileAssociations/RegisteredApplications (ими владеет FileAssociationService).
/// </summary>
public static class MagnetProtocolRegistration
{
    private const string MagnetKey = @"Software\Classes\magnet";
    private const string ProgId = "Prosmotr.Magnet";
    private const string AppName = "Просмотр";
    private const string CapabilitiesKey = @"Software\Prosmotr\Capabilities";

    private static string ExePath =>
        Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName!;

    public static bool IsRegistered
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(MagnetKey);
            return key != null;
        }
    }

    public static void Register()
    {
        var exe = ExePath;

        // 1. ProgID: видимое в «Приложения по умолчанию» имя + иконка.
        using (var progId = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
        {
            progId.SetValue("", "URL:magnet");
            progId.SetValue("URL Protocol", string.Empty);
            using (var icon = progId.CreateSubKey("DefaultIcon"))
                icon.SetValue("", $"\"{exe}\",0");
            using var cmd = progId.CreateSubKey(@"shell\open\command");
            cmd.SetValue("", $"\"{exe}\" \"%1\"");
        }

        // 2. Связка magnet: → ProgID (+ прямая команда как запасная).
        using (var magnet = Registry.CurrentUser.CreateSubKey(MagnetKey))
        {
            magnet.SetValue("", ProgId);
            magnet.SetValue("URL Protocol", string.Empty);
            using var cmd = magnet.CreateSubKey(@"shell\open\command");
            cmd.SetValue("", $"\"{exe}\" \"%1\"");
        }

        // 3. Capabilities\UrlAssociations — ключ для страницы дефолтов Windows 11.
        using (var caps = Registry.CurrentUser.CreateSubKey(CapabilitiesKey))
        {
            caps.SetValue("ApplicationName", AppName);
            caps.SetValue("ApplicationDescription", "Просмотрщик фото, видео и магнет-стриминга");
            using var urlAssoc = caps.CreateSubKey("UrlAssociations");
            urlAssoc.SetValue("magnet", ProgId);
        }

        // 4. RegisteredApplications — чтобы приложение появилось в списке.
        using (var regApps = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
            regApps.SetValue(AppName, CapabilitiesKey);

        NotifyShell();
    }

    public static void Unregister()
    {
        // Только magnet-специфичное. RegisteredApplications/Capabilities\FileAssociations
        // не трогаем — ими владеет FileAssociationService (настройка IntegrateShell).
        TryDelete(() => Registry.CurrentUser.DeleteSubKeyTree(MagnetKey, false));
        TryDelete(() => Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ProgId}", false));
        TryDelete(() =>
        {
            using var caps = Registry.CurrentUser.OpenSubKey(CapabilitiesKey, true);
            using var urlAssoc = caps?.OpenSubKey("UrlAssociations", true);
            urlAssoc?.DeleteValue("magnet", false);
            if (urlAssoc != null && urlAssoc.GetValueNames().Length == 0)
                Registry.CurrentUser.DeleteSubKeyTree($@"{CapabilitiesKey}\UrlAssociations", false);
        });
        NotifyShell();
    }

    private static void TryDelete(Action action)
    {
        try { action(); } catch { /* ключа может не быть */ }
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    private static void NotifyShell() =>
        SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, IntPtr.Zero, IntPtr.Zero);
}
