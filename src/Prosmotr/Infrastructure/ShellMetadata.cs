using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Prosmotr.Infrastructure;

/// <summary>
/// Чтение метаданных файлов через Shell.Application (как в Проводнике) — без
/// декодирования. Длительность видео берётся из свойства System.Media.Duration
/// (в 100-нс тиках), которое отдают установленные в Windows metadata-провайдеры.
/// Нужен STA-поток (см. <see cref="StaTask"/>): Shell-COM апартмент-ниточный,
/// из MTA-пула маршалятся через прокси (см. ExplorerSortReader/RecycleBinRestore).
/// </summary>
public static class ShellMetadata
{
    /// <summary>
    /// Получить длительность видеофайлов папки в миллисекундах. Все элементы галереи
    /// лежат в одной папке, поэтому используется один NameSpace. Возвращает словарь
    /// {имяФайла → мс}; файлы, для которых длительность неизвестна, не включаются.
    /// </summary>
    public static Dictionary<string, long> TryGetDurations(string folder, IEnumerable<string> fileNames)
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return result;

        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType == null) return result;

        dynamic? shell = null;
        dynamic? folderObj = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            if (shell == null) return result;
            folderObj = shell.NameSpace(folder);
            if (folderObj == null) return result;

            foreach (var name in fileNames)
            {
                dynamic? item = null;
                try
                {
                    item = folderObj.ParseName(name);
                    if (item == null) continue;
                    // ExtendedProperty отдаёт строку из 100-нс тиков (напр. "600000000" = 60 c).
                    var raw = item.ExtendedProperty("System.Media.Duration");
                    if (raw == null) continue;
                    if (long.TryParse(raw.ToString(), out long ticks) && ticks > 0)
                        result[name] = ticks / 10000L;
                }
                catch
                {
                    /* отдельный файл мог не отдаться (нет провайдера/доступ) — пропускаем */
                }
                finally
                {
                    Release(item);
                }
            }
        }
        catch
        {
            /* Shell.Application недоступен (политика/нет shell) — отдаём пусто */
        }
        finally
        {
            Release(folderObj);
            Release(shell);
        }
        return result;
    }

    private static void Release(object? obj)
    {
        if (obj == null) return;
        try { Marshal.ReleaseComObject(obj); } catch { /* уже освобождён */ }
    }
}