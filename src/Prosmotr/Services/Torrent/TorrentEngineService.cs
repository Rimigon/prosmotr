using System.IO;
using System.Net;
using System.Windows.Threading;
using MonoTorrent;
using MonoTorrent.Client;
using Prosmotr.Infrastructure;
using Prosmotr.Models;
using Prosmotr.Services.Abstractions;

namespace Prosmotr.Services.Torrent;

/// <summary>
/// Движок магнет-стриминга на MonoTorrent streaming mode.
///
/// Поток данных: MagnetLink → AddStreamingAsync → StartAsync → WaitForMetadataAsync (60 с)
/// → выбор самого большого видеофайла → StreamProvider.CreateStreamAsync(file, prebuffer: true)
/// (скачивает первый и последний куски — штатно решает MP4 с moov в конце) → seekable-поток
/// отдаётся в TorrentSession.Stream, а LibVLC играет его через StreamMediaInput (кастомный IO).
/// Данные при этом пишутся на диск (DiskManager) — кэш + раздача, пока приложение открыто.
///
/// Важно (подтверждено примером LVST): AddStreamingAsync НЕ автозапускает менеджер —
/// обязателен явный StartAsync(), иначе метаданные не скачаются.
/// </summary>
public sealed class TorrentEngineService : ITorrentEngineService, IDisposable
{
    private static readonly TimeSpan MetadataTimeout = TimeSpan.FromSeconds(60);
    /// <summary>Prebuffer (первые+последние куски) тоже может ждать пиров — не вешаемся навсегда.</summary>
    private static readonly TimeSpan PrebufferTimeout = TimeSpan.FromSeconds(90);

    private readonly ISettingsService _settings;
    private readonly object _gate = new();
    private ClientEngine? _engine;
    private TorrentManager? _manager;
    private DispatcherTimer? _progressTimer;
    private CancellationTokenSource? _initCts;

    public TorrentEngineService(ISettingsService settings)
    {
        _settings = settings;
    }

    public TorrentSession? GetActiveSession()
    {
        lock (_gate) return _active;
    }

    private TorrentSession? _active;

    public async Task<TorrentSession> AddMagnetAsync(string magnet, CancellationToken ct)
    {
        AppLog.Write($"[Torrent] AddMagnetAsync start hash={(MagnetLinkParser.TryGetInfoHash(magnet, out var h) ? h : "?")}");
        if (!MagnetLink.TryParse(magnet, out var magnetLink))
        {
            AppLog.Write("[Torrent] AddMagnetAsync: MagnetLink.TryParse FAILED");
            throw new FormatException("Неверная магнет-ссылка.");
        }

        var engine = GetEngine();
        AppLog.Write("[Torrent] Engine ready");
        // В1-хеш для папки кэша; для v2-only магнета — v2 (BEP52).
        var infoHashHex = magnetLink.InfoHashes.V1?.ToHex()
            ?? magnetLink.InfoHashes.V2?.ToHex()
            ?? throw new FormatException("Неверная магнет-ссылка.");
        infoHashHex = infoHashHex.ToLowerInvariant();
        var cacheRoot = _settings.Settings.TorrentCacheDirectory ?? TorrentCachePaths.DefaultCacheDirectory;
        var saveDir = TorrentCachePaths.SaveDirectoryFor(cacheRoot, infoHashHex);
        Directory.CreateDirectory(saveDir);

        lock (_gate)
        {
            if (_active != null)
                throw new InvalidOperationException("Уже есть активная сессия магнет-стриминга.");
        }

        // StartAsync обязателен: AddStreamingAsync только создаёт менеджер (см. LVST).
        // БЕЗ ConfigureAwait(false): сессия/таймер/PropertyChanged должны оставаться на
        // UI-контексте (кросс-тред обновления ObservableObject рвут WPF-биндинги).
        AppLog.Write("[Torrent] AddStreamingAsync...");
        var manager = await engine.AddStreamingAsync(magnetLink, saveDir);
        AppLog.Write("[Torrent] AddStreamingAsync OK, StartAsync...");
        await manager.StartAsync();
        AppLog.Write("[Torrent] StartAsync OK");

        var session = new TorrentSession
        {
            InfoHashHex = infoHashHex,
            SaveDirectory = saveDir,
            EngineRef = manager
        };

        _initCts?.Dispose();
        _initCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lock (_gate)
        {
            _active = session;
            _manager = manager;
        }

        // Инициализация в фоне — сессия уже видна UI в статусе ResolvingMetadata.
        _ = InitSessionAsync(session, manager, _initCts.Token);
        AppLog.Write("[Torrent] Session created, init in background");
        return session;
    }

    private ClientEngine GetEngine()
    {
        lock (_gate)
        {
            if (_engine != null) return _engine;

            var cacheRoot = _settings.Settings.TorrentCacheDirectory ?? TorrentCachePaths.DefaultCacheDirectory;
        _engine = new ClientEngine(new EngineSettingsBuilder
        {
            CacheDirectory = Path.Combine(cacheRoot, ".cache"),
            ListenEndPoints = new Dictionary<string, IPEndPoint>
            {
                ["tcp"] = new(IPAddress.Any, 0) // эфемерный порт; свой порт — вне скоупа v1
            },
            AllowPortForwarding = false, // без UPnP в v1: исходящие + DHT достаточно для старта
            AutoSaveLoadFastResume = true
        }.ToSettings());
            return _engine;
        }
    }

    private async Task InitSessionAsync(TorrentSession session, TorrentManager manager, CancellationToken ct)
    {
        try
        {
            // Метаданные магнет-ссылки приходят от пиров; без пиров — таймаут.
            AppLog.Write("[Torrent] Waiting for metadata...");
            var metadata = manager.WaitForMetadataAsync(ct);
            if (await Task.WhenAny(metadata, Task.Delay(MetadataTimeout, ct)) != metadata)
                throw new TimeoutException("Не удалось получить метаданные (нет пиров).");
            await metadata;
            AppLog.Write($"[Torrent] Metadata OK: {manager.Torrent!.Name}");

            session.Name = manager.Torrent!.Name;
            var selected = TorrentFileSelector.SelectVideoFile(
                    manager.Files.Select(f => new TorrentFileEntry(f.Path, f.Length)))
                ?? throw new InvalidOperationException("В торренте нет видеофайла.");

            // Возвращаем конкретный ITorrentManagerFile для CreateStreamAsync.
            var file = manager.Files.First(f => f.Path == selected.Path);
            session.SelectedFilePath = Path.Combine(session.SaveDirectory!, file.Path);
            session.TotalBytes = file.Length;
            session.Status = TorrentStatus.Downloading;
            // Таймер с момента Downloading: во время prebuffer (ожидание первых кусков)
            // пользователь уже видит %, скорость и пиров — а не статичный 0%.
            StartProgressTimer(session, manager);
            AppLog.Write($"[Torrent] Selected file: {selected.Path} ({selected.Length} bytes)");

            // ВАЖНО (причина «перекачки всего файла при каждом открытии»): MonoTorrent грузит
            // fast-resume только в AddAsync, а для магнета в этот момент метаданных ещё нет
            // (!HasMetadata → MaybeLoadFastResumeAsync выходит рано) — после прихода метаданных
            // загрузка НИКОГДА не повторяется → bitfield всегда пустой. Загружаем вручную:
            // остановка (LoadFastResumeAsync требует Stopped) → загрузка → запуск. Поток ещё
            // не создан — перезапуск менеджера безопасен.
            await LoadSavedFastResumeAsync(manager);

            // prebuffer: true — скачивает первый и последний куски до готовности потока.
            // Это блокирующий вызов: куски должны прийти от пиров (или из fast-resume).
            AppLog.Write("[Torrent] CreateStreamAsync(prebuffer)...");
            var prebuffer = manager.StreamProvider!
                .CreateStreamAsync(file, prebuffer: true, ct);
            var stream = await Task.WhenAny(prebuffer, Task.Delay(PrebufferTimeout, ct)) == prebuffer
                ? await prebuffer
                : throw new TimeoutException("Нет пиров — не удалось получить первые куски для воспроизведения.");
            AppLog.Write("[Torrent] Stream ready, IsReadyToPlay=true");

            session.Stream = stream;
            session.IsReadyToPlay = true;
            session.Status = TorrentStatus.ReadyToPlay;
        }
        catch (OperationCanceledException)
        {
            // Сессию закрыли до готовности — тихо.
            AppLog.Write("[Torrent] InitSession cancelled (session closed)");
        }
        catch (Exception ex)
        {
            AppLog.Error("TorrentEngine.InitSession", ex);
            session.ErrorMessage = UserMessage(ex);
            session.Status = TorrentStatus.Error;
        }
    }

    /// <summary>UI-сообщение для пользователя по типу ошибки (не сырой текст исключения).</summary>
    private static string UserMessage(Exception ex) => ex switch
    {
        TimeoutException => "Не удалось получить данные от пиров — проверьте сеть/раздачу и попробуйте ещё раз.",
        InvalidOperationException => ex.Message,
        IOException => "Не удалось записать файл (нет места на диске?).",
        _ => "Не удалось начать стриминг."
    };

    private void StartProgressTimer(TorrentSession session, TorrentManager manager)
    {
        _progressTimer?.Stop();
        _progressTicks = 0;
        _wasComplete = false;
        _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _progressTimer.Tick += (_, _) =>
        {
            try
            {
                session.DownloadedPercent = manager.Bitfield.PercentComplete;
                session.DownloadSpeed = manager.Monitor.DownloadRate;
                session.UploadSpeed = manager.Monitor.UploadRate;
                session.PeersCount = manager.OpenConnections;
                var remaining = session.TotalBytes > 0
                    ? (long)(session.TotalBytes * (1 - session.DownloadedPercent / 100.0))
                    : 0L;
                session.EtaSeconds = TorrentStats.ComputeEtaSeconds(remaining, manager.Monitor.DownloadRate);

                // Сохранение fast-resume: автосохранение MonoTorrent срабатывает только при
                // успешной остановке, а StopAsync(2s) с таймаутом может её прервать → битфилд
                // терялся и при повторном открытии движок перекачивал ВЕСЬ файл заново.
                // Пишем периодически (каждые 10 с) и сразу при завершении докачки.
                _progressTicks++;
                if (session.DownloadedPercent >= 99.9 && !_wasComplete)
                {
                    _wasComplete = true;
                    _ = SaveFastResumeAsync(manager);
                }
                else if (_progressTicks % 10 == 0)
                {
                    _ = SaveFastResumeAsync(manager);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("TorrentEngine progress", ex);
            }
        };
        _progressTimer.Start();
    }

    private async Task SaveFastResumeAsync(TorrentManager manager)
    {
        try { await manager.SaveFastResumeAsync(); }
        catch (Exception ex) { AppLog.Error("Torrent save fastresume", ex); }
    }

    /// <summary>Ручная загрузка fast-resume после прихода метаданных магнета (см. комментарий
    /// в InitSessionAsync — MonoTorrent сам для магнетов не загружает).</summary>
    private async Task LoadSavedFastResumeAsync(TorrentManager manager)
    {
        var fastResumePath = _engine!.Settings.GetFastResumePath(manager.InfoHashes);
        if (!File.Exists(fastResumePath)) return;
        try
        {
            if (!FastResume.TryLoad(fastResumePath, out var fastResume)) return;
            AppLog.Write($"[Torrent] Loading fastresume: {fastResume.Bitfield.PercentComplete:0.0}%");

            await manager.StopAsync(TimeSpan.FromSeconds(5));
            await manager.LoadFastResumeAsync(fastResume);
            AppLog.Write("[Torrent] FastResume loaded OK");
            await manager.StartAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("Torrent load fastresume", ex);
            // Если остановка сорвалась — хотя бы вернуть менеджер в рабочее состояние.
            try { if (manager.State == TorrentState.Stopped) await manager.StartAsync(); } catch { }
        }
    }

    private int _progressTicks;
    private bool _wasComplete;

    public async Task CloseSessionAsync()
    {
        TorrentSession? session;
        TorrentManager? manager;
        Stream? stream;

        _initCts?.Cancel();
        lock (_gate)
        {
            session = _active;
            manager = _manager;
            stream = session?.Stream;
            _active = null;
            _manager = null;
            if (session != null) session.Stream = null;
        }
        _progressTimer?.Stop();
        if (session == null) return;

        try
        {
            if (manager != null)
            {
                var deleteData = _settings.Settings.DeleteTorrentCacheOnExit;
                // ЯВНО сохраняем fast-resume ДО остановки: автосохранение срабатывает только при
                // успешном переходе в Stopped, а StopAsync(2s) может прервать остановку → битфилд
                // потерян → следующее открытие перекачивает всё заново (см. StartProgressTimer).
                try { await manager.SaveFastResumeAsync(); }
                catch (Exception ex) { AppLog.Error("TorrentEngine close save fastresume", ex); }
                // RemoveAsync требует State == Stopped (иначе TorrentException).
                // StopAsync с таймаутом: не блокируем закрытие приложения дольше 2 с
                // (сеть/пиры могут тормозить остановку менеджера).
                await manager.StopAsync(TimeSpan.FromSeconds(2));
                // KeepAllData: fast-resume и данные остаются (повторный заход продолжает с места).
                // CacheDataAndDownloadedData — только когда пользователь явно попросил очистку.
                await _engine!.RemoveAsync(manager,
                    deleteData ? RemoveMode.CacheDataAndDownloadedData : RemoveMode.KeepAllData);
            }
            stream?.Dispose();
            session.Status = TorrentStatus.Stopped;
        }
        catch (Exception ex)
        {
            AppLog.Error("TorrentEngine.CloseSession", ex);
        }
    }

    public Task ShutdownAsync() => CloseSessionAsync();

    public long GetResumeStartMs(long targetMs, long lengthMs, long totalBytes)
    {
        lock (_gate)
        {
            var manager = _manager;
            var torrent = manager?.Torrent;
            if (manager == null || torrent == null || lengthMs <= 0 || totalBytes <= 0)
                return targetMs;

            // Смещение выбранного файла внутри торрента (для мульти-файловых раздач).
            long fileOffset = 0;
            if (_active?.SelectedFilePath != null && _active.SaveDirectory != null)
            {
                var file = manager.Files.FirstOrDefault(f => string.Equals(
                    Path.Combine(_active.SaveDirectory, f.Path),
                    _active.SelectedFilePath, StringComparison.OrdinalIgnoreCase));
                if (file != null) fileOffset = file.OffsetInTorrent;
            }

            var bitfield = manager.Bitfield;
            var bytesPerMs = (double)totalBytes / lengthMs;
            var targetOffset = fileOffset + (long)(targetMs * bytesPerMs);
            var startPiece = torrent.ByteOffsetToPieceIndex(targetOffset);
            var pieceCount = torrent.PieceCount();
            for (int i = startPiece; i < pieceCount; i++)
            {
                if (bitfield[i])
                {
                    var pieceStart = Math.Max(0, torrent.PieceIndexToByteOffset(i) - fileOffset);
                    return (long)(pieceStart / bytesPerMs);
                }
            }
            return 0; // после целевой точки скачанного нет — начнём с начала
        }
    }

    public void Dispose()
    {
        try { _progressTimer?.Stop(); } catch { }
        try { _initCts?.Cancel(); } catch { }
        try { _initCts?.Dispose(); } catch { }
        // ClientEngine.Dispose может ждать остановки главного цикла/подключений (секунды).
        // На фоне с ограничением — не блокируем закрытие приложения дольше 2 с.
        var engine = _engine;
        _engine = null;
        if (engine != null)
        {
            try
            {
                var t = Task.Run(() => { try { engine.Dispose(); } catch { } });
                t.Wait(TimeSpan.FromSeconds(2));
            }
            catch { /* TimeoutException — выходим без ожидания */ }
        }
    }
}
