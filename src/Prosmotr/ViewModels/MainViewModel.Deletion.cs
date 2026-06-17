using CommunityToolkit.Mvvm.Input;
using Prosmotr.Infrastructure;
using Prosmotr.Models;
using Prosmotr.Services.Abstractions;

namespace Prosmotr.ViewModels;

/// <summary>Часть MainViewModel: удаление файлов и восстановление из Корзины.</summary>
public sealed partial class MainViewModel
{
    // --- Удаление ---

    private bool _isDeleting;
    private MediaItem? _lastDeletedItem;
    private int _lastDeletedIndex;

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
            if (cur.IsVideo && CurrentContent is VideoViewerViewModel videoVm)
            {
                videoVm.IsBuffering = true;
                videoVm.StopAndRelease();
                await Task.Delay(250);
            }

            var result = await _deletion.DeleteAsync(cur.FullPath, permanent);
            if (result.Success)
            {
                // Индекс берём ПО cur, а не _nav.CurrentIndex: пока висел диалог подтверждения
                // (ConfirmAsync не блокирует помпу) или шло удаление, пользователь мог сменить
                // файл стрелками/миниатюрой. Иначе удалили бы из списка не тот элемент, что с диска.
                var index = _nav.IndexOf(cur);
                if (index >= 0) _nav.RemoveAt(index);
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
                    // а при включённой плашке — ещё и кнопкой «Отменить» в самом тосте.
                    _lastDeletedItem = cur;
                    _lastDeletedIndex = index >= 0 ? index : 0;
                    RestoreLastDeleteCommand.NotifyCanExecuteChanged();
                    if (notify)
                        _notify.Show($"«{cur.FileName}» перемещён в корзину.", NotificationKind.Success,
                            "Отменить", () => RestoreLastDeleteCommand.Execute(null));
                }
            }
            else
            {
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
        // Забираем состояние отмены синхронно до await: иначе повторный клик
        // (кнопка есть и на тосте, и на панели) запустит второе восстановление того же файла.
        var item = _lastDeletedItem;
        if (item == null) return;
        var index = _lastDeletedIndex;
        ClearUndoState();

        var ok = await RecycleBinRestore.RestoreAsync(item.FullPath);
        if (ok)
        {
            var restored = _library.CreateItem(item.FullPath) ?? item;
            _nav.InsertAt(restored, index);
            _notify.Show($"«{restored.FileName}» восстановлен.", NotificationKind.Success);
        }
        else
        {
            _notify.Show("Не удалось восстановить файл из корзины.", NotificationKind.Error);
        }
    }

    private bool CanRestore => _lastDeletedItem != null;

    private void ClearUndoState()
    {
        if (_lastDeletedItem == null) return;
        _lastDeletedItem = null;
        RestoreLastDeleteCommand.NotifyCanExecuteChanged();
    }
}
