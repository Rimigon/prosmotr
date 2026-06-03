using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Prosmotr.Infrastructure;
using Prosmotr.Services.Abstractions;

namespace Prosmotr.Services;

/// <summary>Удаление файла в Корзину либо безвозвратно.
/// Корзина — через IFileOperation (Vista+ API), заменивший устаревший SHFileOperation.
/// Критичный флаг FOFX_NOCOPYHOOKS пропускает buggy shell extensions (TortoiseGit,
/// антивирусные хуки и пр.), которые вызывают зависание SHFileOperation.
///
/// Каждое удаление — отдельный STA-поток (COM требует STA для IFileOperation),
/// SemaphoreSlim ограничивает до 1 одновременной операции.
/// Task.WhenAny(tcs.Task, Task.Delay) гарантирует, что при зависании
/// finally { _sem.Release(); } ВСЕГДА отработает — иначе семафор останется
/// захвачен навсегда и удаление полностью умрёт.
/// </summary>
public sealed class FileDeletionService : IFileDeletionService
{
    // Только одна операция одновременно — Explorer/SHELL32 плохо справляются
    // с параллельными COM-вызовами IFileOperation из разных потоков.
    private static readonly SemaphoreSlim _sem = new(1, 1);

    public async Task<bool> DeleteAsync(string path, bool permanent)
    {
        if (!File.Exists(path))
            return false;

        if (permanent)
        {
            try
            {
                File.Delete(path);
                return true;
            }
            catch { return false; }
        }

        await _sem.WaitAsync();
        Thread thread = null!;
        try
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            thread = new Thread(() =>
            {
                try
                {
                    bool ok = MoveToRecycleBinViaIFileOperation(path);
                    tcs.TrySetResult(ok);
                }
                catch (Exception ex)
                {
                    AppLog.Write($"[FileDeletionService] IFileOperation exception: {path} — {ex.Message}");
                    tcs.TrySetResult(false);
                }
            })
            {
                IsBackground = true,
                Name = "Prosmotr.FileDeletion"
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            // ТАЙМАУТ = 10 с. Главный фикс: Task.WhenAny позволяет finally с _sem.Release()
            // выполниться ДАЖЕ если IFileOperation завис в STA-потоке.
            const int timeoutSec = 10;
            var winner = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(timeoutSec)));
            if (winner == tcs.Task)
            {
                return await tcs.Task;
            }

            AppLog.Write($"[FileDeletionService] IFileOperation TIMEOUT ({timeoutSec}s): {path} — STA thread stuck, abandoning");

            // Пытаемся дождаться завершения потока, чтобы не оставлять зомби.
            if (!thread.Join(TimeSpan.FromSeconds(15)))
            {
                try { thread.Interrupt(); } catch { }
            }

            return false;
        }
        finally
        {
            _sem.Release();
        }
    }

    private static bool MoveToRecycleBinViaIFileOperation(string path)
    {
        FileOperationInterop.IFileOperation? fileOp = null;
        FileOperationInterop.IShellItem? shellItem = null;
        try
        {
            var fileOpType = Type.GetTypeFromCLSID(FileOperationInterop.CLSID_FileOperation)
                ?? throw new InvalidOperationException("CLSID_FileOperation not available");
            fileOp = (FileOperationInterop.IFileOperation)Activator.CreateInstance(fileOpType)!;

            uint flags = FileOperationInterop.FOF_ALLOWUNDO      // в Корзину
                       | FileOperationInterop.FOF_NOCONFIRMATION  // без "Вы уверены?"
                       | FileOperationInterop.FOF_SILENT          // без прогресса
                       | FileOperationInterop.FOF_NOERRORUI     // без окна об ошибке
                       | FileOperationInterop.FOFX_NOCOPYHOOKS;  // <— пропускаем buggy shell extensions!

            fileOp.SetOperationFlags(flags);

            FileOperationInterop.SHCreateItemFromParsingName(
                path,
                IntPtr.Zero,
                FileOperationInterop.IID_IShellItem,
                out shellItem);

            fileOp.DeleteItem(shellItem, IntPtr.Zero);

            uint hr = fileOp.PerformOperations();
            fileOp.GetAnyOperationsAborted(out bool aborted);

            AppLog.Write($"[FileDeletionService] IFileOperation hr=0x{hr:X8}, aborted={aborted}, path={path}");
            return hr == 0 && !aborted;
        }
        catch (Exception ex)
        {
            AppLog.Write($"[FileDeletionService] MoveToRecycleBinViaIFileOperation exception: {ex}");
            return false;
        }
        finally
        {
            if (shellItem != null)
                try { Marshal.ReleaseComObject(shellItem); } catch { }
            if (fileOp != null)
                try { Marshal.ReleaseComObject(fileOp); } catch { }
        }
    }
}
