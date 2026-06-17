using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using MediaRendering = System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using Prosmotr.Infrastructure;
using Prosmotr.Models;
using Prosmotr.Services;
using Prosmotr.Services.Abstractions;

namespace Prosmotr.ViewModels;

/// <summary>Вариант скорости воспроизведения для меню.</summary>
public sealed record RateOption(float Value, string Label);

/// <summary>Пункт меню выбора аудиодорожки/субтитров.</summary>
public sealed record TrackChoice(int Id, string Name, bool IsCurrent);

/// <summary>
/// Просмотр видео через LibVLC. Локальная скорость для текущего файла + глобальная по умолчанию,
/// громкость/mute, перемотка, resume позиции, индикатор скорости.
/// </summary>
public sealed partial class VideoViewerViewModel : ViewModelBase, IDisposable
{
    private static readonly float[] Rates =
        { 0.25f, 0.5f, 0.75f, 1f, 1.25f, 1.5f, 1.75f, 2f, 2.5f, 3f, 4f, 5f };

    private readonly VideoPlaybackService _playback;
    private readonly ISettingsService _settings;
    private readonly IPlaybackPositionStore _positions;
    private readonly IDialogService _dialog;
    private readonly INotificationService _notify;

    private float _pendingRate = 1f;
    private bool _started;
    private bool _disposed;
    // На время переключения видео→видео подавляем SavePosition: асинхронные события
    // старого плеера (Stopped/TimeChanged) иначе запишут позицию под путём нового файла.
    private bool _switching;
    // Поколение отложенной загрузки (LoadAndPlayDeferred): инкремент на каждый Start/SwitchTo/Replay.
    // Победила последняя — устаревшие отложенные Load/Play (от быстрой навигации) пропускаются.
    private int _loadGen;

    public MediaItem Item { get; private set; }
    public MediaPlayer Player => _playback.Player;
    public IReadOnlyList<RateOption> AvailableRates { get; }

    /// <summary>Максимум громкости (с усилением до 300 %).</summary>
    public int MaxVolume => VideoPlaybackService.MaxVolume;

    /// <summary>Скрывать ли панель/курсор по таймеру бездействия (настройка AutoHideControls).</summary>
    public bool AutoHideControls => _settings.Settings.AutoHideControls;

    /// <summary>Включено ли усиление (громкость выше 100 %).</summary>
    public bool IsBoosted => Volume > 100;

    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private double _positionMs;
    [ObservableProperty] private double _lengthMs;
    [ObservableProperty] private int _volume = 100;
    [ObservableProperty] private bool _isMuted;
    [ObservableProperty] private float _rate = 1f;
    [ObservableProperty] private string _rateText = "1×";
    [ObservableProperty] private bool _isEnded;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private bool _showRateBadge;
    [ObservableProperty] private RateOption? _selectedRate;

    /// <summary>Показывать боковые кнопки перехода к пред./след. файлу (есть больше одного файла).</summary>
    [ObservableProperty] private bool _showFileNavigation;

    /// <summary>Идёт загрузка первого кадра (старт или переключение видео). View показывает
    /// чёрный cover поверх нативного HWND LibVLC, чтобы скрыть его светлый фон (белый квадрат),
    /// мелькающий до отрисовки первого кадра. false — первый кадр реально обновился (TimeChanged
    /// дал ненулевое время) либо ошибка.</summary>
    [ObservableProperty] private bool _isBuffering;

    // OnPlaying приходит слишком рано — раньше первого видимого кадра. TimeChanged тоже
    // не годится: при resume-старте оно приходит сразу с большим временем, но кадр ещё не
    // отрисован. Поэтому cover убираем по таймеру после OnPlaying — даём LibVLC фиксированное
    // окно (400 мс) на отрисовку первого кадра.
    private DispatcherTimer? _firstFrameTimer;
    private bool _suppressRateSelect;

    public VideoViewerViewModel(
        MediaItem item,
        LibVlcProvider provider,
        ISettingsService settings,
        IPlaybackPositionStore positions,
        IDialogService dialog,
        INotificationService notify)
    {
        Item = item;
        _settings = settings;
        _positions = positions;
        _dialog = dialog;
        _notify = notify;
        _playback = new VideoPlaybackService(provider);

        AvailableRates = Rates.Select(r => new RateOption(r, FormatRate(r))).ToList();

        // Начальные значения из настроек (без обращения к плееру — применим при старте).
        _volume = Math.Clamp(_settings.Settings.LastVolume, 0, VideoPlaybackService.MaxVolume);
        _isMuted = _settings.Settings.LastMuted;

        var p = _playback.Player;
        p.Playing += OnPlaying;
        p.Paused += OnPaused;
        p.Stopped += OnStopped;
        p.EndReached += OnEndReached;
        p.EncounteredError += OnError;
        p.TimeChanged += OnTimeChanged;
        p.LengthChanged += OnLengthChanged;

        // Свежий VM вот-вот начнёт загрузку: сразу отмечаем «буферизацию», чтобы View показал
        // чёрный cover ещё до первого OnLoaded/Start (и скрыл белый фон нативного HWND).
        IsBuffering = true;
    }

    /// <summary>Запускается из View после загрузки VideoView (когда готов нативный HWND).</summary>
    public void Start()
    {
        if (_started || _disposed) return;
        _started = true;
        BeginPlayback();
    }

    /// <summary>
    /// Переключиться на другое видео БЕЗ пересоздания плеера/окна — плавно.
    /// Вызывается при навигации видео→видео.
    /// </summary>
    public void SwitchTo(MediaItem item)
    {
        if (_disposed) return;

        SavePosition();           // сохранить позицию текущего видео
        _switching = true;        // далее SavePosition подавлен до старта нового видео
        Item = item;
        OnPropertyChanged(nameof(Item));
        IsEnded = false;
        HasError = false;
        PositionMs = 0;
        LengthMs = 0;

        // Поднимаем чёрный cover ДО остановки старой дорожки: StopAndRelease очищает
        // нативное HWND LibVLC, и без cover его светлый фон мелькает между видео.
        // View (тот же экземпляр при video→video) увидит IsBuffering=true и закроет
        // белый фон раньше, чем плеер начнёт освобождать Media.
        IsBuffering = true;
        _playback.StopAndRelease();
        BeginPlayback();
    }

    private void BeginPlayback()
    {
        // Cover встаёт ДО смены Media/Play: нативное окно LibVLC при загрузке новой дорожки
        // успевает мигнуть светлым фоном. Чёрный cover в оверлее перекрывает его, пока не
        // отрисован первый реальный кадр (TimeChanged > 0) либо ошибка.
        IsBuffering = true;

        var stored = _positions.Get(Item.FullPath);

        long startMs = 0;
        if (_settings.Settings.ResumeVideoPosition && stored is { PositionMs: > 5000, DurationMs: > 0 }
            && stored.PositionMs < stored.DurationMs - 5000)
        {
            startMs = stored.PositionMs;
        }

        // Скорость: глобальная по умолчанию либо запомненная для файла.
        _pendingRate = _settings.Settings.DefaultPlaybackRate;
        if (_settings.Settings.RememberRatePerFile && stored?.Rate is > 0)
            _pendingRate = stored.Rate!.Value;

        LoadAndPlayDeferred(startMs);
    }

    /// <summary>Загрузить дорожку и запустить воспроизведение ТОЛЬКО ПОСЛЕ того, как WPF
    /// отрисовал чёрный cover. Используем CompositionTarget.Rendering: подписываемся на один
    /// кадр рендера, затем небольшую задержку (50 мс), и только потом Load/Play. Без этого
    /// нативное окно LibVLC (класс "static") успевало мигнуть белым фоном раньше cover.
    /// Поколение _loadGen защищает от устаревших отложенных загрузок при быстрой навигации.
    /// </summary>
    private void LoadAndPlayDeferred(long startMs)
    {
        var gen = ++_loadGen;
        var path = Item.FullPath;
        var app = Application.Current;
        if (app == null) { _playback.Load(path, startMs); _playback.Play(); return; }

        AppLog.Write($"[Flicker] LoadAndPlayDeferred queued gen={gen} path={Path.GetFileName(path)}");
        EventHandler? renderingHandler = null;
        var renderSw = Stopwatch.StartNew();
        renderingHandler = (_, _) =>
        {
            MediaRendering.CompositionTarget.Rendering -= renderingHandler;
            AppLog.Write($"[Flicker] Render frame ready gen={gen} after {renderSw.ElapsedMilliseconds} ms, scheduling 50ms delay");
            var delay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            delay.Tick += (_, _) =>
            {
                delay.Stop();
                if (_disposed || gen != _loadGen) { AppLog.Write($"[Flicker] Superseded gen={gen}"); return; }
                AppLog.Write($"[Flicker] Loading gen={gen} total delay {renderSw.ElapsedMilliseconds} ms");
                _playback.Load(path, startMs);
                _playback.Play();
            };
            delay.Start();
        };
        app.Dispatcher.BeginInvoke(new Action(() => MediaRendering.CompositionTarget.Rendering += renderingHandler), DispatcherPriority.Render);
    }

    // --- Команды управления ---

    [RelayCommand]
    private void TogglePlay()
    {
        if (IsEnded) { Replay(); return; }
        _playback.TogglePause();
    }

    [RelayCommand]
    private void ToggleMute()
    {
        IsMuted = !IsMuted;
    }

    partial void OnSelectedRateChanged(RateOption? value)
    {
        if (value != null && !_suppressRateSelect)
            ApplyRate(value.Value, flashBadge: true);
    }

    /// <summary>Установить скорость воспроизведения (из меню кнопки скорости).</summary>
    public void SetRate(float value) => ApplyRate(value, flashBadge: true);

    [RelayCommand] private void VolumeUp() => Volume = Math.Clamp(Volume + 5, 0, VideoPlaybackService.MaxVolume);
    [RelayCommand] private void VolumeDown() => Volume = Math.Clamp(Volume - 5, 0, VideoPlaybackService.MaxVolume);

    private double StepMs => Math.Max(1, _settings.Settings.SeekStepSeconds) * 1000.0;
    // Длительность одного кадра; если FPS ещё неизвестен — берём ~25 кадров/с (40 мс).
    private double FrameMs { get { var fps = _playback.Fps; return fps > 0 ? 1000.0 / fps : 40.0; } }

    [RelayCommand]
    private void StepForward()
    {
        if (_settings.Settings.FrameByFrameSeek)
            _playback.NextFrame();                                 // ровно один кадр вперёд (с паузой)
        else
            SeekTo(Math.Min(PositionMs + StepMs, LengthMs));
    }

    [RelayCommand]
    private void StepBackward()
    {
        if (_settings.Settings.FrameByFrameSeek)
        {
            _playback.Pause();                                     // покадрово — на паузе, как и вперёд
            SeekTo(Math.Max(PositionMs - FrameMs, 0));
        }
        else
        {
            SeekTo(Math.Max(PositionMs - StepMs, 0));
        }
    }

    [RelayCommand]
    private void Replay()
    {
        IsEnded = false;
        IsBuffering = true; // cover до первого кадра (как при старт/переключении)
        LoadAndPlayDeferred(0);
    }

    /// <summary>Изменить скорость для текущего видео (вызывается клавишами [ ] / +/-).</summary>
    public void NudgeRate(int direction)
    {
        var idx = Array.FindIndex(Rates, r => Math.Abs(r - Rate) < 0.001f);
        if (idx < 0) idx = Array.IndexOf(Rates, 1f);
        idx = Math.Clamp(idx + Math.Sign(direction), 0, Rates.Length - 1);
        ApplyRate(Rates[idx], flashBadge: true);
    }

    /// <summary>Перемотка на позицию в миллисекундах (вызывается из таймлайна).</summary>
    public void SeekTo(double ms)
    {
        var clamped = Math.Clamp(ms, 0, Math.Max(0, LengthMs));
        _playback.Time = (long)clamped;
        PositionMs = clamped;
        // Позицию не сохраняем здесь: каждый seek/shuttle-шаг иначе запускал бы
        // debounce-запись на диск и микро-задержки. Сохранение происходит при паузе,
        // остановке, завершении видео и при выходе из viewer (Dispose).
        if (IsEnded && clamped < LengthMs) IsEnded = false;
    }

    // --- Аудиодорожки, субтитры, снимок кадра ---

    /// <summary>
    /// Остановить плеер и освободить Media для удаляемого файла.
    /// VM остаётся живым — UpdateCurrentContent сам переключит на следующий файл.
    /// </summary>
    public void StopAndRelease()
    {
        if (_disposed) return;
        _playback.StopAndRelease();
    }

    /// <summary>Текущие аудиодорожки видео (для меню кнопки звука).</summary>
    public IReadOnlyList<TrackChoice> GetAudioTracks()
    {
        var current = _playback.CurrentAudioTrack;
        return _playback.AudioTracks
            .Select(t => new TrackChoice(t.Id, TrackName(t.Id, t.Name), t.Id == current))
            .ToList();
    }

    /// <summary>Доступные субтитры; первым пунктом — «Отключить» (id = -1).</summary>
    public IReadOnlyList<TrackChoice> GetSubtitleTracks()
    {
        var current = _playback.CurrentSubtitle;
        var result = new List<TrackChoice> { new(-1, "Отключить", current == -1) };
        foreach (var t in _playback.SubtitleTracks)
            if (t.Id != -1)
                result.Add(new TrackChoice(t.Id, TrackName(t.Id, t.Name), t.Id == current));
        return result;
    }

    public void SelectAudioTrack(int id) => _playback.SetAudioTrack(id);
    public void SelectSubtitle(int id) => _playback.SetSubtitle(id);

    [RelayCommand]
    private void LoadSubtitle()
    {
        var path = _dialog.OpenFile(new[] { ".srt", ".ass", ".ssa", ".sub", ".vtt", ".idx" });
        if (path == null) return;
        if (_playback.AddSubtitleFile(path, select: true))
            _notify.Show("Субтитры подключены.", NotificationKind.Success);
        else
            _notify.Show("Не удалось подключить субтитры.", NotificationKind.Error);
    }

    [RelayCommand]
    private void TakeSnapshot()
    {
        try
        {
            var name = Path.GetFileNameWithoutExtension(Item.FileName);
            var target = Path.Combine(Item.DirectoryPath, $"{name}_кадр_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            if (_playback.TakeSnapshot(target))
                _notify.Show("Кадр сохранён рядом с видео.", NotificationKind.Success);
            else
                _notify.Show("Не удалось сохранить кадр.", NotificationKind.Error);
        }
        catch
        {
            _notify.Show("Не удалось сохранить кадр.", NotificationKind.Error);
        }
    }

    private static string TrackName(int id, string? name) =>
        id == -1 ? "Отключить" : string.IsNullOrWhiteSpace(name) ? $"Дорожка {id}" : name!;

    private void ApplyRate(float value, bool flashBadge)
    {
        _pendingRate = value;
        _playback.Rate = value;
        Rate = value;
        RateText = FormatRate(value);

        // Синхронизируем выбранный пункт ComboBox без рекурсии.
        _suppressRateSelect = true;
        SelectedRate = AvailableRates.FirstOrDefault(o => Math.Abs(o.Value - value) < 0.001f);
        _suppressRateSelect = false;

        if (_settings.Settings.RememberRatePerFile)
            SavePosition();

        if (flashBadge) _ = FlashRateBadgeAsync(); // fire-and-forget
    }

    private async Task FlashRateBadgeAsync()
    {
        ShowRateBadge = true;
        try { await Task.Delay(1200); } catch { /* Ignore */ }
        ShowRateBadge = false;
    }

    // --- Реакция на изменение наблюдаемых свойств ---

    partial void OnVolumeChanged(int value)
    {
        _playback.Volume = value;
        _settings.Settings.LastVolume = value;
        _settings.SaveDebounced();
        OnPropertyChanged(nameof(IsBoosted));
    }

    partial void OnIsMutedChanged(bool value)
    {
        _playback.Mute = value;
        _settings.Settings.LastMuted = value;
        _settings.SaveDebounced();
    }

    // --- События плеера (приходят из потока LibVLC — маршалим в UI) ---

    private void OnPlaying(object? sender, EventArgs e) => OnUi(() =>
    {
        // BeginInvoke мог поставить эту лямбду в очередь до Dispose; отписка не отменяет
        // уже поставленное. Без guard'а ниже мы бы писали Rate/Volume/Mute в уже уничтоженный
        // нативный MediaPlayer → AccessViolation. (как в OnTimeChanged/OnLengthChanged)
        if (_disposed) return;
        AppLog.Write($"[Flicker] OnPlaying fired gen={_loadGen}, IsBuffering={IsBuffering}");
        IsPlaying = true;
        IsEnded = false;
        _switching = false; // новое видео реально стартовало — снимаем подавление SavePosition
        // НЕ сбрасываем IsBuffering здесь: OnPlaying приходит раньше реального кадра.
        // Cover убираем по таймеру — даём LibVLC 400 мс на отрисовку первого кадра.
        _firstFrameTimer?.Stop();
        _firstFrameTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _firstFrameTimer.Tick += (_, _) =>
        {
            _firstFrameTimer?.Stop();
            if (!_disposed && IsBuffering)
            {
                AppLog.Write($"[Flicker] First-frame timer elapsed gen={_loadGen} — clearing cover");
                IsBuffering = false;
            }
        };
        _firstFrameTimer.Start();
        // Применяем отложенные параметры, когда воспроизведение реально стартовало.
        _playback.Rate = _pendingRate;
        Rate = _pendingRate;
        RateText = FormatRate(_pendingRate);
        _playback.Volume = Volume;
        _playback.Mute = IsMuted;
    });

    private void OnPaused(object? sender, EventArgs e) => OnUi(() => { if (_disposed) return; IsPlaying = false; SavePosition(); });
    private void OnStopped(object? sender, EventArgs e) => OnUi(() => { if (_disposed) return; IsPlaying = false; SavePosition(); });
    private void OnError(object? sender, EventArgs e) => OnUi(() => { if (_disposed) return; AppLog.Write($"[Flicker] OnError gen={_loadGen}"); HasError = true; IsPlaying = false; IsBuffering = false; });

    private void OnEndReached(object? sender, EventArgs e) => OnUi(() =>
    {
        if (_disposed) return;
        IsPlaying = false;
        IsEnded = true;
        PositionMs = LengthMs;
        SavePosition();
        _positions.Remove(Item.FullPath); // досмотрено — позицию не храним
    });


    private void OnLengthChanged(object? sender, MediaPlayerLengthChangedEventArgs e) =>
        OnUi(() => { if (!_disposed) LengthMs = e.Length; });

    private void SavePosition()
    {
        if (_disposed || _switching || IsEnded || LengthMs <= 0) return;
        var time = (long)PositionMs;
        if (time <= 1000) return;
        float? rate = _settings.Settings.RememberRatePerFile ? Rate : null;
        _positions.Save(Item.FullPath, time, (long)LengthMs, rate);
    }

    private static string FormatRate(float r) =>
        r.ToString("0.##", CultureInfo.CurrentCulture) + "×";

    private void OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e) =>
        OnUi(() => { if (!_disposed) PositionMs = e.Time; });

    private static void OnUi(Action action)
    {
        var app = Application.Current;
        if (app == null) { action(); return; }
        if (app.Dispatcher.CheckAccess()) { action(); return; }
        try { app.Dispatcher.BeginInvoke(action); }
        catch (InvalidOperationException) { /* Dispatcher shutting down — игнорируем */ }
    }

    public void Dispose()
    {
        if (_disposed) return;

        IsBuffering = false; // на всякий случай убираем cover (View отвязывается параллельно)

        // SavePosition ДО _disposed=true: сама SavePosition имеет ранний return при _disposed,
        // поэтому при обратном порядке это был no-op и resume-позиция терялась при video→фото/выходе.
        // НО только если файл ещё существует: при удалении видео отложенный Dispose иначе воскресил бы
        // resume-запись, которую Delete уже убрал (_positions.Remove).
        if (File.Exists(Item.FullPath)) SavePosition();
        _disposed = true;

        var p = _playback.Player;
        p.Playing -= OnPlaying;
        p.Paused -= OnPaused;
        p.Stopped -= OnStopped;
        p.EndReached -= OnEndReached;
        p.EncounteredError -= OnError;
        p.TimeChanged -= OnTimeChanged;
        p.LengthChanged -= OnLengthChanged;

        _playback.Dispose();
    }
}
