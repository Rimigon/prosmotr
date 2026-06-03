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
        RunStaAsync(() => Restore(originalPath));

    private static bool Restore(string originalPath)
    {
        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType == null) return false;

        dynamic? shell = Activator.CreateInstance(shellType);
        if (shell == null) return false;

        try
        {
            dynamic recycleBin = shell.NameSpace(RecycleBinFolder);
            dynamic items = recycleBin.Items();
            int count = items.Count;

            dynamic? best = null;
            DateTime bestDate = DateTime.MinValue;

            for (int i = 0; i < count; i++)
            {
                dynamic item = items.Item(i);
                if (!MatchesPath(item, originalPath)) continue;

                var date = TryGetDeletedDate(item);
                if (best == null || date >= bestDate)
                {
                    best = item;
                    bestDate = date;
                }
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
            try { Marshal.FinalReleaseComObject(shell); } catch { }
        }
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
        int verbCount = verbs.Count;
        dynamic? first = null;

        for (int i = 0; i < verbCount; i++)
        {
            dynamic verb = verbs.Item(i);
            if (i == 0) first = verb;

            string normalized = ((string)verb.Name).Replace("&", "").Trim().ToLowerInvariant();
            if (Array.IndexOf(RestoreVerbNames, normalized) >= 0)
            {
                verb.DoIt();
                return;
            }
        }

        first?.DoIt();
    }

    private static Task<bool> RunStaAsync(Func<bool> work)
    {
        var tcs = new TaskCompletionSource<bool>();
        var thread = new Thread(() =>
        {
            try { tcs.SetResult(work()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}
