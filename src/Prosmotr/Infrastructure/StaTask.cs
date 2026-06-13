using System.Threading;

namespace Prosmotr.Infrastructure;

/// <summary>
/// Запуск работы на выделенном STA-потоке. Нужен для Shell-COM (ExplorerSortReader,
/// RecycleBinRestore): эти объекты апартмент-нитевые и из MTA-потоков пула (Task.Run)
/// маршалятся через прокси, что даёт нестабильные результаты / RPC_E_WRONG_THREAD.
/// </summary>
public static class StaTask
{
    public static Task<T> Run<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { tcs.SetResult(work()); }
            catch (Exception ex) { tcs.SetException(ex); }
        })
        {
            IsBackground = true,
            Name = "Prosmotr.Sta"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}
