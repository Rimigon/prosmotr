using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Prosmotr.Infrastructure;
using Prosmotr.Models;
using Prosmotr.Services.Abstractions;
using Prosmotr.Services.Torrent;

namespace Prosmotr.ViewModels;

/// <summary>Часть MainViewModel: магнет-стриминг (входы, сессия, закрытие).</summary>
public sealed partial class MainViewModel
{
    private readonly ITorrentEngineService _torrents;
    private readonly IRecentMagnetsService _recentMagnets;
    private readonly Func<TorrentSession, Func<Task>, TorrentStreamViewModel> _torrentVmFactory;
    private readonly CancellationTokenSource _torrentCts = new();

    /// <summary>Просьба показать диалог вставки магнет-ссылки (обрабатывает MainWindow).</summary>
    public event Action? MagnetInputRequested;

    /// <summary>Просьба показать диалог кэша магнет-стриминга (обрабатывает MainWindow).</summary>
    public event Action? TorrentCacheRequested;

    [RelayCommand]
    private void OpenMagnet() => MagnetInputRequested?.Invoke();

    [RelayCommand]
    private void OpenTorrentCache() => TorrentCacheRequested?.Invoke();

    /// <summary>
    /// Открыть сессию магнет-стриминга по ссылке (кнопка стартового экрана, буфер обмена,
    /// magnet: аргумент). Сессия возвращается движком сразу (ResolvingMetadata) и сама
    /// дозревает до ReadyToPlay — UI показывает прогресс.
    /// </summary>
    public async Task OpenMagnetAsync(string magnet)
    {
        AppLog.Write($"[Torrent] OpenMagnetAsync called, valid={MagnetLinkParser.IsValidMagnet(magnet)}");
        if (!MagnetLinkParser.IsValidMagnet(magnet))
        {
            _notify.Show("Неверная магнет-ссылка.", NotificationKind.Warning);
            return;
        }

        if (_activePipWindow != null) ClosePictureInPicture();

        try
        {
            var session = await _torrents.AddMagnetAsync(magnet, _torrentCts.Token);
            AppLog.Write($"[Torrent] Session created: {session.InfoHashHex}");
            // Недавние магнет-ссылки на стартовом экране (имя из &dn= ссылки).
            _recentMagnets.Add(magnet, MagnetLinkParser.GetDisplayName(magnet));
            var vm = _torrentVmFactory(session, CloseTorrentSessionAsync);
            // Полный экран торрент-плеера — через тот же безопасный путь, что у фото/видео
            // (gotcha 5.12/5.16: переход НЕ синхронно внутри Click — DeferFullScreenTransition).
            vm.FullScreenRequested += ToggleFullScreen;
            CurrentContent = vm;
        }
        catch (FormatException)
        {
            AppLog.Write("[Torrent] OpenMagnetAsync: FormatException");
            _notify.Show("Неверная магнет-ссылка.", NotificationKind.Warning);
        }
        catch (OperationCanceledException)
        {
            // Сессия отменена (закрытие) — тихо.
        }
        catch (Exception ex)
        {
            AppLog.Error("OpenMagnetAsync", ex);
            _notify.Show("Не удалось начать стриминг.", NotificationKind.Error);
        }
    }

    /// <summary>Закрыть торрент-сессию и вернуться на стартовый экран (кнопка/клавиша Esc).</summary>
    private async Task CloseTorrentSessionAsync()
    {
        if (CurrentContent is TorrentStreamViewModel vm)
        {
            vm.StopAndRelease(); // плеер освобождается ДО закрытия потока движком
            CurrentContent = CreateEmptyState();
        }
        await _torrents.CloseSessionAsync();
    }

    /// <summary>Пустой старт: в буфере обмена валидная магнет-ссылка → предложить диалог.</summary>
    private void TryOfferClipboardMagnet()
    {
        try
        {
            if (Clipboard.ContainsText() && MagnetLinkParser.IsValidMagnet(Clipboard.GetText()))
                MagnetInputRequested?.Invoke();
        }
        catch (Exception ex)
        {
            // Буфер обмена (COM) может быть недоступен в RDP/без фокуса — не критично.
            AppLog.Error("TryOfferClipboardMagnet", ex);
        }
    }
}
