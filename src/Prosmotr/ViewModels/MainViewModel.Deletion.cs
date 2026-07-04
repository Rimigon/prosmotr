using CommunityToolkit.Mvvm.Input;
using System.Windows;
using System.Windows.Threading;
using Prosmotr.Infrastructure;
using Prosmotr.Models;
using Prosmotr.Services.Abstractions;

namespace Prosmotr.ViewModels;

/// <summary>Часть MainViewModel: удаление файлов и восстановление из Корзины.</summary>
public sealed partial class MainViewModel
{
    // --- Удаление ---

    private bool _isDeleting;
    private bool _isRestoring;

    /// <summary>Стек удалённых в Корзину файлов: последний элемент — последнее удаление.</summary>
    private readonly List<DeletedItem> _deletedStack = new();

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task Delete()
    {
        if (_isDeleting) return;
        var cur = _nav.Current;
        if (cur == null) return;

        _isDeleting = true;
        DeleteCommand.NotifyCanExecuteChanged();
        try
        {
            var permanent = _settings.Settings.PermanentDelete;

            if (_settings.Settings.ConfirmDelete)
            {
                var msg = permanent
                    ? $"Удалить «{cur.FileName}» безвозвратно?"
                    : $"Переместить «{cur.FileName}» в Корзину?";
                var confirmed = await _dialog.ConfirmAsync("Удаление файла", msg, "Удалить", "Отмена");
                if (!confirmed) return;
            }

            // Видео держит файловый handle в LibVLC. Останавливаем плеер и освобождаем Media,
            // чтобы файл можно было переместить в Корзину / удалить без sharing violation.
            // Поднимаем чёрный cover ДО остановки: в полноэкранном режиме StopAndRelease очищает
            // нативное HWND LibVLC, и его светлый фон на секунду заливает весь экран. Cover в
            // оверлее ForegroundWindow скроет этот промежуток до SwitchTo/переключения.
            if (cur.IsVideo)
            {
                if (_pipSourceVm?.Item.FullPath == cur.FullPath)
                {
                    ClosePictureInPicture();
                }
                else if (CurrentContent is VideoViewerViewModel videoVm)
                {
                    videoVm.IsBuffering = true;
                    videoVm.StopAndRelease();
                    await Task.Delay(250);
                }
            }

            ImageViewerViewModel? imageVm = null;
            if (cur.IsAnimated && CurrentContent is ImageViewerViewModel ivm)
            {
                // XamlAnimatedGif держит FileStream открытым во время анимации.
                // Освобождаем handle перед удалением, иначе IFileOperation не сможет
                // переместить файл (aborted=True).
                imageVm = ivm;
                imageVm.RequestReleaseFileHandle();
            }

            var result = await _deletion.DeleteAsync(cur.FullPath, permanent);
            if (result.Success)
            {
                // Индекс берём ПО cur, а не _nav.CurrentIndex: пока висел диалог подтверждения
                // (ConfirmAsync не блокирует помпу) или шло удаление, пользователь мог сменить
                // файл стрелками/миниатюрой. Иначе удалили бы из списка не тот элемент, что с диска.
                var index = _nav.IndexOf(cur);
                if (index >= 0) _nav.RemoveAt(index);

                // Dispose GIF ViewModel после RemoveAt, чтобы old Animator не пытался
                // перезагрузить файл при переключении на следующий элемент.
                if (imageVm != null)
                {
                    _pendingDisposal = imageVm;
                    Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        imageVm.Dispose();
                        if (ReferenceEquals(_pendingDisposal, imageVm)) _pendingDisposal = null;
                    }, DispatcherPriority.Render);
                }

                // Remove ПОСЛЕ RemoveAt: переключение на следующий файл (SwitchTo) или disposal
                // старого плеера синхронно вызывает SavePosition для удаляемого файла, что
                // воссоздало бы его resume-запись. Чистим её после.
                _positions.Remove(cur.FullPath);

                var notify = _settings.Settings.ShowDeleteNotification;
                if (permanent)
                {
                    ClearUndoState();
                    if (notify)
                        _notify.Show($"«{cur.FileName}» удалён навсегда.", NotificationKind.Success);
                }
                else
                {
                    // Запоминаем для отмены. Восстановить можно кнопкой на панели,
                    // контекстным меню или кнопкой «Отменить» в тосте; каждое нажатие
                    // восстанавливает следующий файл из стека в порядке, обратном удалению.
                    _deletedStack.Add(new DeletedItem(cur, index >= 0 ? index : 0));
                    RestoreLastDeleteCommand.NotifyCanExecuteChanged();
                    if (notify)
                        _notify.Show($"«{cur.FileName}» перемещён в корзину.", NotificationKind.Success,
                            "Отменить", () => RestoreLastDeleteCommand.Execute(null));
                }
            }
            else
            {
                // Удаление не удалось — восстанавливаем анимацию GIF, если handle был освобождён.
                imageVm?.RequestRestoreFileHandle();
                _notify.Show(result.ErrorMessage ?? "Не удалось удалить файл.", NotificationKind.Error);
            }
        }
        finally
        {
            _isDeleting = false;
            DeleteCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanDelete => !_isDeleting && _nav.Current != null;

    // --- Отмена удаления (восстановление из Корзины) ---

    [RelayCommand(CanExecute = nameof(CanRestore))]
    private async Task RestoreLastDelete()
    {
        // Защита от повторного входа: сама команда остаётся enabled, пока стек не пуст,
        // но параллельно восстанавливаться не должны — иначе COM/Explorer может зависнуть.
        if (_isRestoring) return;
        if (_deletedStack.Count == 0) return;

        _isRestoring = true;
        RestoreLastDeleteCommand.NotifyCanExecuteChanged();

        // Забираем состояние отмены синхронно до await: иначе повторный клик
        // (кнопка есть и на тосте, и на панели) запустил бы второе восстановление того же файла.
        var entry = _deletedStack[_deletedStack.Count - 1];
        _deletedStack.RemoveAt(_deletedStack.Count - 1);
        RestoreLastDeleteCommand.NotifyCanExecuteChanged();

        AppLog.Write($"RestoreLastDelete start: {entry.Item.FileName}, index={entry.Index}, stackCount={_deletedStack.Count + 1}");
        try
        {
            var ok = await RecycleBinRestore.RestoreAsync(entry.Item.FullPath);
            AppLog.Write($"RestoreLastDelete result: {entry.Item.FileName} -> {(ok ? "OK" : "FAIL")}");
            if (ok)
            {
                var restored = _library.CreateItem(entry.Item.FullPath) ?? entry.Item;
                _nav.InsertAt(restored, entry.Index);
                _notify.Show($"«{restored.FileName}» восстановлен.", NotificationKind.Success);
            }
            else
            {
                // Восстановление не удалось — возвращаем запись обратно в стек,
                // чтобы пользователь мог попробовать ещё раз.
                _deletedStack.Add(entry);
                _notify.Show("Не удалось восстановить файл из корзины.", NotificationKind.Error);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("RestoreLastDelete", ex);
            _deletedStack.Add(entry);
            _notify.Show("Не удалось восстановить файл из корзины.", NotificationKind.Error);
        }
        finally
        {
            _isRestoring = false;
            RestoreLastDeleteCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanRestore => _deletedStack.Count > 0;

    private void ClearUndoState()
    {
        if (_deletedStack.Count == 0) return;
        _deletedStack.Clear();
        RestoreLastDeleteCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Снимок удалённого файла: элемент и его индекс на момент удаления.</summary>
    private sealed record DeletedItem(MediaItem Item, int Index);
}
