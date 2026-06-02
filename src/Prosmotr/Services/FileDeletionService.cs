using System.IO;
using System.Threading;
using Prosmotr.Infrastructure;
using Prosmotr.Services.Abstractions;

namespace Prosmotr.Services;

/// <summary>Удаление файла в Корзину (через оболочку Windows) либо безвозвратно.</summary>
public sealed class FileDeletionService : IFileDeletionService
{
    /// <summary>
    /// Shell-операция <c>SHFileOperation</c> (перемещение в Корзину) надёжно работает только в
    /// STA-апартаменте. Если вызывать её из MTA-потока пула (<see cref="System.Threading.Tasks.Task.Run(System.Action)"/>),
    /// после нескольких удалений подряд она начинает периодически отказывать (возвращать ненулевой код) —
    /// отсюда симптом «после 3–5 файлов перестаёт удалять». Поэтому каждое удаление выполняем на
    /// выделенном STA-потоке — так же, как восстановление из Корзины в <see cref="RecycleBinRestore"/>.
    /// </summary>
    public Task<bool> DeleteAsync(string path, bool permanent)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            bool result;
            // SHFileOperation в Windows 11 требует инициализации COM/OLE в STA-потоке,
            // иначе после 3–4 вызовов операция замирает навсегда.
            NativeMethods.OleInitialize(IntPtr.Zero);
            try
            {
                if (!File.Exists(path))
                {
                    result = false;
                }
                else if (permanent)
                {
                    File.Delete(path);
                    result = true;
                }
                else
                {
                    result = NativeMethods.MoveToRecycleBin(path);
                }
            }
            catch
            {
                result = false;
            }
            finally
            {
                NativeMethods.OleUninitialize();
            }
            tcs.SetResult(result);
        })
        {
            IsBackground = true,
            Name = "Prosmotr.FileDeletion"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return tcs.Task;
    }
}
