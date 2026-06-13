using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Prosmotr.Infrastructure;

/// <summary>
/// Восстановление файла из Корзины по его исходному пути. Использует COM-автоматизацию
/// оболочки (<c>Shell.Application</c>, пространство имён Корзины = 10): находит элемент по
/// свойствам «исходная папка» + имя и вызывает у него глагол «Восстановить».
/// </summary>
/// <remarks>
/// Имя глагола локализовано (рус. «Восстановить», англ. «Restore») — матчим по набору имён,
/// иначе берём первый глагол (для элементов Корзины это обычно и есть восстановление).
/// COM работает только в STA — выполняем на отдельном STA-потоке.
/// </remarks>
public static class RecycleBinRestore
{
    private const int RecycleBinFolder = 10;

    private static readonly string[] RestoreVerbNames = { "восстановить", "restore" };

    /// <summary>Восстановить файл из Корзины обратно по пути <paramref name="originalPath"/>.</summary>
    public static Task<bool> RestoreAsync(string originalPath) =>
        StaTask.Run(() => Restore(originalPath));

    private static bool Restore(string originalPath)
    {
        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType == null) return false;

        dynamic? shell = Activator.CreateInstance(shellType);
        if (shell == null) return false;

        dynamic? recycleBin = null;
        dynamic? items = null;
        dynamic? best = null;
        try
        {
            recycleBin = shell.NameSpace(RecycleBinFolder);
            items = recycleBin.Items();
            int count = items.Count;

            DateTime bestDate = DateTime.MinValue;

            for (int i = 0; i < count; i++)
            {
                dynamic item = items.Item(i);
                bool keep = false;
                if (MatchesPath(item, originalPath))
                {
                    var date = TryGetDeletedDate(item);
                    if (best == null || date >= bestDate)
                    {
                        Release(best);      // прежний кандидат больше не нужен
                        best = item;
                        bestDate = date;
                        keep = true;
                    }
                }
                if (!keep) Release(item);   // не подошёл — освобождаем сразу (иначе утечка RCW)
            }

            if (best == null) return false;

            InvokeRestore(best);

            // Восстановление асинхронно — ждём появления файла (до ~2 сек).
            for (int i = 0; i < 20 && !File.Exists(originalPath); i++)
                Thread.Sleep(100);

            return File.Exists(originalPath);
        }
        catch (Exception ex)
        {
            AppLog.Error("RecycleBinRestore", ex);
            return false;
        }
        finally
        {
            // Освобождаем все промежуточные RCW на этом же STA-потоке до его завершения.
            Release(best);
            Release(items);
            Release(recycleBin);
            try { Marshal.FinalReleaseComObject(shell); } catch { }
        }
    }

    /// <summary>Безопасно освободить COM-RCW (без throw).</summary>
    private static void Release(object? comObject)
    {
        if (comObject != null)
            try { Marshal.ReleaseComObject(comObject); } catch { }
    }

    private static bool MatchesPath(dynamic item, string originalPath)
    {
        try
        {
            string name = item.Name;
            string from = item.ExtendedProperty("System.Recycle.DeletedFrom");
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(name)) return false;
            var full = Path.GetFullPath(Path.Combine(from, name));
            var normalizedOriginal = Path.GetFullPath(originalPath);
            return string.Equals(full, normalizedOriginal, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static DateTime TryGetDeletedDate(dynamic item)
    {
        try
        {
            object raw = item.ExtendedProperty("System.Recycle.DateDeleted");
            return raw is DateTime dt ? dt : DateTime.MinValue;
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private static void InvokeRestore(dynamic item)
    {
        dynamic verbs = item.Verbs();
        dynamic? first = null;
        try
        {
            int verbCount = verbs.Count;
            for (int i = 0; i < verbCount; i++)
            {
                dynamic verb = verbs.Item(i);
                if (i == 0) first = verb; // первый глагол освобождаем в finally

                string normalized = ((string)verb.Name).Replace("&", "").Trim().ToLowerInvariant();
                if (Array.IndexOf(RestoreVerbNames, normalized) >= 0)
                {
                    verb.DoIt();
                    if (i != 0) Release(verb);
                    return;
                }
                if (i != 0) Release(verb); // не first и не подошёл — освобождаем сразу
            }

            first?.DoIt(); // fallback: для элементов Корзины первый глагол — «Восстановить»
        }
        finally
        {
            Release(first);
            Release(verbs);
        }
    }
}
