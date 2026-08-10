using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using Prosmotr.Infrastructure;
using Prosmotr.Models;
using Prosmotr.Services;
using Prosmotr.Services.Abstractions;
using Prosmotr.Services.Torrent;

namespace Prosmotr.ViewModels;

/// <summary>
/// VM экрана «магнет-стриминг»: фаза загрузки (прогресс из TorrentSession) и фаза
/// воспроизведения (LibVLC играет поток MonoTorrent через StreamMediaInput — кастомный IO).
/// Управление — как в основном плеере: скорость (с памятью на файл через PlaybackPositionStore),
/// громкость с бейджем, аудиодорожки, субтитры (встроенные + внешний файл).
/// Паттерны плеера повторяют VideoViewerViewModel: EnableHardwareDecoding=false,
/// StopAndRelease до освобождения потока, ползунок в пределах скачанного.
/// </summary>
public sealed partial class TorrentStreamViewModel : ViewModelBase, IDisposable
{
    private static readonly float[] Rates =
        { 0.25f, 0.5f, 0.75f, 1f, 1.25f, 1.5f, 1.75f, 2f, 2.5f, 3f, 4f };

    private readonly TorrentSession _session;
    private readonly ITorrentEngineService _torrents;
    private readonly LibVlcProvider _vlc;
    private readonly ISettingsService _settings;
    private readonly IPlaybackPositionStore _positions;
    private readonly IDialogService _dialog;
    private readonly INotificationService _notify;
    private readonly Func<Task> _closeRequested;

    private MediaPlayer? _player;
    private Media? _media;
    private bool _disposed;
    private float _pendingRate = 1f;
    private long _resumeMs;
    private int? _pendingAudioTrackId;
    private string? _pendingAudioTrackName;

    [ObservableProperty] private bool _isBuffering;
    [ObservableProperty] private bool _isPlaying;
    // double (не long): конвертер MsToTime и слайдер таймлайна работают с double, как в VideoViewerViewModel.
    [ObservableProperty] private double _positionMs;
    [ObservableProperty] private double _lengthMs;
    [ObservableProperty] private int _volume;
    [ObservableProperty] private bool _isMuted;
    [ObservableProperty] private float _rate = 1f;
    [ObservableProperty] private string _rateText = "1×";
    [ObservableProperty] private bool _showRateBadge;
    [ObservableProperty] private bool _showVolumeBadge;
    [ObservableProperty] private string _volumeBadgeText = "100%";

    /// <summary>Готовность плеера: вью подключает его к VideoView до StartPlayback().</summary>
    public MediaPlayer? Player => _player;

    public IReadOnlyList<RateOption> AvailableRates { get; }

    public event Action? FullScreenRequested;

    public TorrentStreamViewModel(
        TorrentSession session,
        ITorrentEngineService torrents,
        LibVlcProvider vlc,
        ISettingsService settings,
        IPlaybackPositionStore positions,
        IDialogService dialog,
        INotificationService notify,
        Func<Task> closeRequested)
    {
        _session = session;
        _torrents = torrents;
        _vlc = vlc;
        _settings = settings;
        _positions = positions;
        _dialog = dialog;
        _notify = notify;
        _closeRequested = closeRequested;
        _session.PropertyChanged += OnSessionPropertyChanged;
        AvailableRates = Rates.Select(r => new RateOption(r, FormatRate(r))).ToList();
    }

    // --- Прокси-свойства сессии (XAML биндится сюда, а не в MonoTorrent-типы) ---

    public string? Name => _session.Name;
    public bool IsReadyToPlay => _session.IsReadyToPlay;
    public bool IsDownloading => _session.Status is TorrentStatus.ResolvingMetadata or TorrentStatus.Downloading;
    /// <summary>Поиск метаданных (нет ещё ни процента, ни пиров) — неопределённая полоса.</summary>
    public bool IsSearching => _session.Status == TorrentStatus.ResolvingMetadata;
    public bool IsError => _session.Status == TorrentStatus.Error;
    public string? ErrorMessage => _session.ErrorMessage;
    public double DownloadedPercent => _session.DownloadedPercent;
    public string SpeedText => $"{TorrentStats.FormatBytes(_session.DownloadSpeed)}/с";
    public string UploadText => $"{TorrentStats.FormatBytes(_session.UploadSpeed)}/с";
    public string PeersText => $"{_session.PeersCount}";
    /// <summary>Компактная строка для плашки «скачивание» в углу плеера.</summary>
    public string DownloadSummaryText =>
        $"{DownloadedPercent:0}% · {TorrentStats.FormatBytes(_session.DownloadSpeed)}/с ↓ · {_session.PeersCount} пиров";
    public string EtaText => _session.EtaSeconds is long eta
        ? $"Осталось ~{FormatEta(eta)}"
        : "Оценка недоступна";

    public string StatusText => _session.Status switch
    {
        TorrentStatus.ResolvingMetadata => "Поиск пиров…",
        TorrentStatus.Downloading => "Загрузка…",
        TorrentStatus.ReadyToPlay => "Готово к воспроизведению",
        TorrentStatus.Playing => "Воспроизведение",
        TorrentStatus.Error => "Ошибка",
        _ => string.Empty
    };

    /// <summary>Автоскрытие панели управления (настройка AutoHideControls, читается на лету).</summary>
    public bool AutoHideControls => _settings.Settings.AutoHideControls;

    private void OnSessionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Только целевые прокси по имени изменившегося свойства. Раньше поднимались ВСЕ разом,
        // и таймер движка (5 свойств/сек) переподнимал IsReadyToPlay → вью заново звало Play()
        // → пачка Play() в LibVLC → зависание (см. 5.36).
        switch (e.PropertyName)
        {
            case nameof(TorrentSession.Name):
                OnPropertyChanged(nameof(Name));
                break;
            case nameof(TorrentSession.IsReadyToPlay):
                OnPropertyChanged(nameof(IsReadyToPlay));
                break;
            case nameof(TorrentSession.Status):
                OnPropertyChanged(nameof(IsReadyToPlay));
                OnPropertyChanged(nameof(IsDownloading));
                OnPropertyChanged(nameof(IsSearching));
                OnPropertyChanged(nameof(IsError));
                OnPropertyChanged(nameof(StatusText));
                break;
            case nameof(TorrentSession.ErrorMessage):
                OnPropertyChanged(nameof(ErrorMessage));
                break;
            case nameof(TorrentSession.DownloadedPercent):
                OnPropertyChanged(nameof(DownloadedPercent));
                OnPropertyChanged(nameof(DownloadSummaryText));
                UpdateBuffering();
                break;
            case nameof(TorrentSession.DownloadSpeed):
                OnPropertyChanged(nameof(SpeedText));
                OnPropertyChanged(nameof(DownloadSummaryText));
                break;
            case nameof(TorrentSession.UploadSpeed):
                OnPropertyChanged(nameof(UploadText));
                break;
            case nameof(TorrentSession.PeersCount):
                OnPropertyChanged(nameof(PeersText));
                OnPropertyChanged(nameof(DownloadSummaryText));
                break;
            case nameof(TorrentSession.EtaSeconds):
                OnPropertyChanged(nameof(EtaText));
                break;
        }
    }

    // --- Воспроизведение ---

    /// <summary>Создать плеер и подготовить медиа (НЕ запускать). Вызывается вью при IsReadyToPlay
    /// ДО привязки к VideoView: LibVLC должен получить HWND до Play(), иначе vout уйдёт в отдельное окно.</summary>
    public void CreatePlayer()
    {
        if (_disposed || _player != null || !_session.IsReadyToPlay) return;
        if (_session.Stream == null) return;

        var player = new MediaPlayer(_vlc.LibVlc)
        {
            // Консистентно с основным плеером: аппаратное декодирование на ряде GPU
            // даёт зелёный экран.
            EnableHardwareDecoding = false
        };
        _player = player;
        // Кастомный IO: VLC сам тянет данные из MonoTorrent-потока (блокирующее чтение
        // — поток качает нужные куски на лету). Больший входной буфер заставляет VLC читать
        // дальше вперёд, давая requester'у запас времени на докачку кусков (меньше стопов).
        _media = new Media(_vlc.LibVlc, new StreamMediaInput(_session.Stream));
        _media.AddOption(":file-caching=2000");
        _media.AddOption(":network-caching=2000");

        player.TimeChanged += OnTimeChanged;
        player.LengthChanged += OnLengthChanged;
        player.Playing += OnPlaying;
        player.Paused += OnPaused;
        player.Stopped += OnStopped;
        player.EndReached += OnEndReached;
        player.EncounteredError += OnEncounteredError;

        // Восстановление из памяти (ключ PlaybackPositionStore — путь к файлу в кэше,
        // стабильный для infoHash): скорость, аудиодорожка, позиция (resume).
        _pendingRate = ClampRate(_settings.Settings.DefaultPlaybackRate);
        _pendingAudioTrackId = null;
        _pendingAudioTrackName = null;
        _resumeMs = 0;
        if (_session.SelectedFilePath != null)
        {
            var stored = _positions.Get(_session.SelectedFilePath);
            if (stored != null)
            {
                if (_settings.Settings.RememberRatePerFile && stored.Rate is > 0)
                    _pendingRate = ClampRate(stored.Rate!.Value);
                if (_settings.Settings.RememberAudioTrackPerFile && stored.AudioTrackId is int trackId)
                {
                    _pendingAudioTrackId = trackId;
                    _pendingAudioTrackName = stored.AudioTrackName;
                }
                if (_settings.Settings.ResumeVideoPosition && stored.PositionMs > 5000)
                    _resumeMs = stored.PositionMs;
            }
        }

        Volume = _settings.Settings.LastVolume;
        IsMuted = _settings.Settings.LastMuted;

        AppLog.Write("[Torrent] Player created");
        // Cover до первого кадра: нативный HWND мог ещё не отрисовать кадр — не показываем белый фон.
        IsBuffering = true;
    }

    /// <summary>Запустить воспроизведение. Вызывается вью ПОСЛЕ Video.MediaPlayer = Player.
    /// Строго однократно: повторный Play() в LibVLC во время старта вешает плеер.</summary>
    private bool _playbackStarted;

    public void StartPlayback()
    {
        if (_disposed || _player == null || _media == null || _playbackStarted) return;
        _playbackStarted = true;
        var ok = _player.Play(_media);
        AppLog.Write($"[Torrent] Play() -> {ok}");
        _session.Status = TorrentStatus.Playing;
    }

    /// <summary>Выполнить действие на UI-потоке. События LibVLCSharp приходят с потоков libvlc
    /// (в 3.9.7.1 нет маршалинга через SynchronizationContext — проверено по исходникам) —
    /// трогать VM/WPF-объекты с них нельзя (кросс-тред InvalidOperationException).</summary>
    private void OnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted) return;
        if (dispatcher.CheckAccess()) { action(); return; }
        dispatcher.BeginInvoke(action);
    }

    private void OnPlaying(object? sender, EventArgs e)
    {
        if (_disposed) return;
        var length = _player?.Length ?? 0;
        var time = _player?.Time ?? 0;
        var state = _player?.Media?.State;
        OnUi(() =>
        {
            if (_disposed) return;
            AppLog.Write($"[Torrent] VLC Playing event: Length={length}ms Time={time}ms State={state}");
            IsPlaying = true;
            _session.Status = TorrentStatus.Playing;
            // Применяем сохранённые параметры ТОЛЬКО при реальном изменении (gotcha 5.34:
            // безусловный SetRate/SetVolume при каждом resume перезапускал аудиовыход —
            // на Bluetooth звук пропадал на секунду). Свежая загрузка сбрасывает их в дефолты —
            // guard пропускает применение.
            try
            {
                if (Math.Abs(_player!.Rate - _pendingRate) > 0.001f)
                    _player.SetRate(_pendingRate);
                if (_player.Volume != Math.Clamp(Volume, 0, VideoPlaybackService.MaxVolume))
                    _player.Volume = Math.Clamp(Volume, 0, VideoPlaybackService.MaxVolume);
                if (_player.Mute != IsMuted)
                    _player.Mute = IsMuted;
            }
            catch (Exception ex)
            {
                AppLog.Error("Torrent OnPlaying apply params", ex);
            }

            // Синхронизируем UI с применённой скоростью: иначе кнопка/панель показывали бы «1×»,
            // хотя воспроизведение идёт на запомненной скорости.
            Rate = _pendingRate;
            RateText = FormatRate(_pendingRate);

            // Восстановление озвучки: список дорожек LibVLC отдаёт только после старта (gotcha 5.34).
            if (_settings.Settings.RememberAudioTrackPerFile && _pendingAudioTrackId is int audioId)
            {
                var matched = MatchAudioTrack(audioId, _pendingAudioTrackName);
                if (matched is int realId)
                {
                    try { _player!.SetAudioTrack(realId); } catch { }
                }
            }
            _pendingAudioTrackId = null;
            _pendingAudioTrackName = null;

            // Resume: НЕ сразу — откладываем на ~1.5 с (пусть отрисуется первый кадр)
            // и только если позиция в пределах уже скачанного. Иначе стриминг-поток
            // блокируется на недоскачанных кусках → «чёрный экран».
            if (_resumeMs > 0)
            {
                var target = _resumeMs;
                _resumeMs = 0;
                _ = DelayedResumeAsync(target);
            }

            // Первый кадр готов — снимаем cover, буферизацию дальше считаем по позиции.
            IsBuffering = false;
            UpdateBuffering();
        });
    }

    /// <summary>Отложенная перемотка к сохранённой позиции. Перематываем ТОЛЬКО на полностью
    /// скачанный кусок: LocalStream.ReadAsync блокируется на недокачанных (поллит Bitfield
    /// по 100 мс) → VLC встаёт и «подлагивает». Seek выполняется НЕ на UI-потоке: libvlc
    /// set_time может блокировать, если входной поток застрял на чтении → иначе UI замрёт
    /// без индикаторов.</summary>
    private async Task DelayedResumeAsync(long targetMs)
    {
        try { await Task.Delay(1500); } catch { return; }
        if (_disposed || _player == null || !_player.IsPlaying) return;

        var safeMs = _torrents.GetResumeStartMs(targetMs, (long)LengthMs, _session.TotalBytes);
        if (safeMs <= 0)
        {
            AppLog.Write($"[Torrent] Resume SKIPPED: no complete piece at/after {targetMs}ms");
            return;
        }

        var player = _player;
        var seek = Task.Run(() => { try { player.Time = safeMs; } catch { } });
        var done = await Task.WhenAny(seek, Task.Delay(TimeSpan.FromSeconds(5)));
        if (done != seek)
        {
            // Входной поток застрял (недокачанный кусок) — не вешаем UI, пропускаем resume.
            AppLog.Write($"[Torrent] Resume seek to {safeMs}ms TIMED OUT (input stalled)");
            return;
        }
        PositionMs = safeMs;
        AppLog.Write($"[Torrent] Resume seek -> {safeMs}ms (requested {targetMs}ms)");
    }

    private void OnEncounteredError(object? sender, EventArgs e)
    {
        if (_disposed) return;
        var state = _player?.Media?.State;
        OnUi(() =>
        {
            if (_disposed) return;
            AppLog.Write($"[Torrent] VLC EncounteredError, media state={state}");
            IsBuffering = false;
            IsPlaying = false;
            _session.ErrorMessage = "Не удалось воспроизвести поток.";
            _session.Status = TorrentStatus.Error;
        });
    }

    private void OnPaused(object? sender, EventArgs e)
    {
        if (_disposed) return;
        OnUi(() =>
        {
            if (_disposed) return;
            IsPlaying = false;
            SavePosition();
        });
    }

    private void OnStopped(object? sender, EventArgs e)
    {
        if (_disposed) return;
        OnUi(() =>
        {
            if (_disposed) return;
            IsPlaying = false;
            SavePosition();
        });
    }

    private void OnEndReached(object? sender, EventArgs e)
    {
        // Следующего файла нет — просто останавливаемся на конце (как у финального файла).
        if (_disposed) return;
        OnUi(() =>
        {
            if (_disposed) return;
            IsPlaying = false;
            SavePosition();
        });
    }

    private bool _loggedFirstTime;
    private bool _loggedFirstLength;

    private void OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
    {
        if (_disposed) return;
        var time = e.Time;
        OnUi(() =>
        {
            if (_disposed) return;
            if (!_loggedFirstTime)
            {
                _loggedFirstTime = true;
                AppLog.Write($"[Torrent] first TimeChanged: {time}ms");
            }
            PositionMs = time;
            UpdateBuffering();
        });
    }

    private void OnLengthChanged(object? sender, MediaPlayerLengthChangedEventArgs e)
    {
        if (_disposed) return;
        var length = e.Length;
        OnUi(() =>
        {
            if (_disposed) return;
            if (!_loggedFirstLength)
            {
                _loggedFirstLength = true;
                AppLog.Write($"[Torrent] first LengthChanged: {length}ms");
            }
            LengthMs = length;
            UpdateBuffering();
        });
    }

    /// <summary>Оверлей «Докачивается…»: позиция VLC ушла за границу скачанного (с запасом).</summary>
    private void UpdateBuffering()
    {
        if (_disposed) return;
        IsBuffering = TorrentStats.IsBeyondDownloaded(
            (long)PositionMs, (long)LengthMs, _session.DownloadedPercent, slackMs: 3000);
    }

    // --- Управление ---

    [RelayCommand]
    private void TogglePlayPause()
    {
        if (_player == null || _disposed) return;
        if (_player.IsPlaying) _player.Pause();
        else _player.Play();
    }

    /// <summary>Перемотка. За границей скачанного поток заблокирует чтение — покажется
    /// оверлей «Докачивается…», движок качает с этой позиции (свойство LocalStream).</summary>
    [RelayCommand]
    private void SeekTo(double ms)
    {
        if (_player == null || _disposed || ms < 0) return;
        _player.Time = (long)Math.Clamp(ms, 0, LengthMs);
    }

    [RelayCommand]
    private void ToggleMute()
    {
        IsMuted = !IsMuted;
    }

    [RelayCommand]
    private void VolumeUp() => Volume = Math.Clamp(Volume + 5, 0, VideoPlaybackService.MaxVolume);

    [RelayCommand]
    private void VolumeDown() => Volume = Math.Clamp(Volume - 5, 0, VideoPlaybackService.MaxVolume);

    partial void OnVolumeChanged(int value)
    {
        if (_disposed) return;
        if (_player != null) _player.Volume = Math.Clamp(value, 0, VideoPlaybackService.MaxVolume);
        _settings.Settings.LastVolume = Math.Clamp(value, 0, VideoPlaybackService.MaxVolume);
        _settings.SaveDebounced();
        VolumeBadgeText = FormatVolumeBadge(value, IsMuted);
        _ = FlashVolumeBadgeAsync();
    }

    partial void OnIsMutedChanged(bool value)
    {
        if (_disposed) return;
        if (_player != null) _player.Mute = value;
        _settings.Settings.LastMuted = value;
        _settings.SaveDebounced();
        VolumeBadgeText = FormatVolumeBadge(Volume, value);
        _ = FlashVolumeBadgeAsync();
    }

    partial void OnIsBufferingChanged(bool value)
    {
        // Диагностика подлагиваний: переходы буферизации с позицией и % скачанного.
        if (value)
            AppLog.Write($"[Torrent] Buffering START: pos={PositionMs:0}ms downloaded={_session.DownloadedPercent:0.0}%");
        else
            AppLog.Write($"[Torrent] Buffering END: pos={PositionMs:0}ms");
    }

    // --- Скорость (как в основном плеере; память на файл — PlaybackPositionStore) ---

    public void SetRate(float value) => ApplyRate(value, flashBadge: true);

    /// <summary>Следующая/предыдущая скорость из списка (для кнопки-цикла).</summary>
    public void NudgeRate(int direction)
    {
        var idx = Array.FindIndex(Rates, r => Math.Abs(r - Rate) < 0.001f);
        if (idx < 0) idx = Array.IndexOf(Rates, 1f);
        idx = Math.Clamp(idx + Math.Sign(direction), 0, Rates.Length - 1);
        ApplyRate(Rates[idx], flashBadge: true);
    }

    private void ApplyRate(float value, bool flashBadge)
    {
        var clamped = ClampRate(value);
        _pendingRate = clamped;
        if (_player != null)
        {
            try { _player.SetRate(clamped); } catch (Exception ex) { AppLog.Error("Torrent SetRate", ex); }
        }
        Rate = clamped;
        RateText = FormatRate(clamped);
        if (_settings.Settings.RememberRatePerFile)
            SavePosition();
        if (flashBadge) _ = FlashRateBadgeAsync();
    }

    private float ClampRate(float value) => Math.Clamp(value, Rates[0], Rates[^1]);

    private static string FormatRate(float r) =>
        r.ToString("0.##", CultureInfo.CurrentCulture) + "×";

    // --- Аудиодорожки и субтитры (списки доступны только после старта воспроизведения) ---

    public IReadOnlyList<TrackChoice> GetAudioTracks()
    {
        if (_player == null) return Array.Empty<TrackChoice>();
        var current = _player.AudioTrack;
        return _player.AudioTrackDescription
            .Select(t => new TrackChoice(t.Id, TrackName(t.Id, t.Name), t.Id == current))
            .ToList();
    }

    public IReadOnlyList<TrackChoice> GetSubtitleTracks()
    {
        if (_player == null) return new List<TrackChoice> { new(-1, "Отключить", true) };
        var current = _player.Spu;
        var result = new List<TrackChoice> { new(-1, "Отключить", current == -1) };
        foreach (var t in _player.SpuDescription)
            if (t.Id != -1)
                result.Add(new TrackChoice(t.Id, TrackName(t.Id, t.Name), t.Id == current));
        return result;
    }

    public void SelectAudioTrack(int id)
    {
        if (_player == null) return;
        try { _player.SetAudioTrack(id); } catch { }
        if (_settings.Settings.RememberAudioTrackPerFile)
            SavePosition();
    }

    public void SelectSubtitle(int id)
    {
        if (_player == null) return;
        try { _player.SetSpu(id); } catch { }
    }

    [RelayCommand]
    private void LoadSubtitle()
    {
        if (_player == null) return;
        var path = _dialog.OpenFile(new[] { ".srt", ".ass", ".ssa", ".sub", ".vtt", ".idx" });
        if (path == null) return;
        // AddSlave требует URI; экранирование спецсимволов — общий хелпер PathUri (gotcha 5.22).
        var uri = PathUri.ToUri(path);
        if (_player.AddSlave(MediaSlaveType.Subtitle, uri.AbsoluteUri, select: true))
            _notify.Show("Субтитры подключены.", NotificationKind.Success);
        else
            _notify.Show("Не удалось подключить субтитры.", NotificationKind.Error);
    }

    private string? FindAudioTrackName(int id)
    {
        if (_player == null) return null;
        foreach (var t in _player.AudioTrackDescription)
            if (t.Id == id) return t.Name;
        return null;
    }

    /// <summary>Сопоставление сохранённой дорожки: сначала по id, запасной поиск по имени
    /// (id может сместиться, если файл пересобран). foreach, не Array.Find — TrackDescription
    /// не nullable-аннотирован (gotcha 5.34).</summary>
    private int? MatchAudioTrack(int? id, string? name)
    {
        if (_player == null) return null;
        if (id is int trackId)
        {
            foreach (var t in _player.AudioTrackDescription)
                if (t.Id == trackId) return t.Id;
        }
        if (!string.IsNullOrWhiteSpace(name))
        {
            foreach (var t in _player.AudioTrackDescription)
                if (string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) return t.Id;
        }
        return null;
    }

    private static string TrackName(int id, string? name) =>
        string.IsNullOrWhiteSpace(name) ? $"Дорожка {id}" : name;

    // --- Сохранение (resume + скорость + озвучка на файл) ---

    /// <summary>Сохранить позицию/скорость/аудиодорожку для этого файла (ключ — путь в кэше).</summary>
    private void SavePosition()
    {
        if (_disposed || _player == null || LengthMs <= 0) return;
        if (_session.SelectedFilePath == null) return;
        var time = (long)PositionMs;
        if (time <= 1000) return;
        float? rate = _settings.Settings.RememberRatePerFile ? Rate : null;
        int? audioId = _settings.Settings.RememberAudioTrackPerFile && _player.AudioTrack > 0
            ? _player.AudioTrack : null;
        string? audioName = audioId is int id ? FindAudioTrackName(id) : null;
        _positions.Save(_session.SelectedFilePath, time, (long)LengthMs, rate, audioId, audioName);
    }

    // --- Бейджи ---

    private async Task FlashRateBadgeAsync()
    {
        ShowRateBadge = true;
        try { await Task.Delay(1200); } catch { }
        ShowRateBadge = false;
    }

    private async Task FlashVolumeBadgeAsync()
    {
        ShowVolumeBadge = true;
        try { await Task.Delay(1200); } catch { }
        ShowVolumeBadge = false;
    }

    private static string FormatVolumeBadge(int volume, bool muted) =>
        muted ? "Звук выкл" : $"{volume}%";

    private static string FormatEta(long seconds)
    {
        if (seconds < 60) return $"{seconds} с";
        if (seconds < 3600) return $"{seconds / 60} мин";
        return $"{seconds / 3600} ч {seconds % 3600 / 60} мин";
    }

    [RelayCommand]
    private void ToggleFullScreen() => FullScreenRequested?.Invoke();

    [RelayCommand]
    private Task CloseSession() => _closeRequested();

    /// <summary>Остановить плеер и освободить Media ДО закрытия сессии движком
    /// (иначе VLC держал бы поток, а движок его уже закрыл).</summary>
    public void StopAndRelease()
    {
        if (_disposed) return;
        SavePosition();
        _disposed = true;
        _session.PropertyChanged -= OnSessionPropertyChanged;

        if (_player != null)
        {
            _player.TimeChanged -= OnTimeChanged;
            _player.LengthChanged -= OnLengthChanged;
            _player.Playing -= OnPlaying;
            _player.Paused -= OnPaused;
            _player.Stopped -= OnStopped;
            _player.EndReached -= OnEndReached;
            _player.EncounteredError -= OnEncounteredError;
            try { _player.Stop(); } catch { }
            _player.Dispose();
            _player = null;
        }
        _media?.Dispose();
        _media = null;
    }

    public void Dispose() => StopAndRelease();
}
