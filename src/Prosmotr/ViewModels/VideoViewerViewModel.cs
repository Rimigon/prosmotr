using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
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

        BeginPlayback();          // загрузить новую дорожку в тот же плеер
    }

    private void BeginPlayback()
    {
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

        _playback.Load(Item.FullPath, startMs);
        _playback.Play();
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
        _playback.Load(Item.FullPath, 0);
        _playback.Play();
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
        SavePosition();
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
        IsPlaying = true;
        IsEnded = false;
        _switching = false; // новое видео реально стартовало — снимаем подавление SavePosition
        // Применяем отложенные параметры, когда воспроизведение реально стартовало.
        _playback.Rate = _pendingRate;
        Rate = _pendingRate;
        RateText = FormatRate(_pendingRate);
        _playback.Volume = Volume;
        _playback.Mute = IsMuted;
    });

    private void OnPaused(object? sender, EventArgs e) => OnUi(() => { if (_disposed) return; IsPlaying = false; SavePosition(); });
    private void OnStopped(object? sender, EventArgs e) => OnUi(() => { if (_disposed) return; IsPlaying = false; SavePosition(); });
    private void OnError(object? sender, EventArgs e) => OnUi(() => { if (_disposed) return; HasError = true; IsPlaying = false; });

    private void OnEndReached(object? sender, EventArgs e) => OnUi(() =>
    {
        if (_disposed) return;
        IsPlaying = false;
        IsEnded = true;
        PositionMs = LengthMs;
        SavePosition();
        _positions.Remove(Item.FullPath); // досмотрено — позицию не храним
    });

    private void OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e) =>
        OnUi(() => { if (!_disposed) PositionMs = e.Time; });

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
