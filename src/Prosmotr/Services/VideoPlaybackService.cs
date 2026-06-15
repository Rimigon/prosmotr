using LibVLCSharp.Shared;
using LibVLCSharp.Shared.Structures;

namespace Prosmotr.Services;

/// <summary>
/// Обёртка над LibVLC MediaPlayer для одного видео. Создаётся на время просмотра
/// и освобождается при переходе к другому файлу (обходит баги VideoView при Unload/Reload).
/// </summary>
public sealed class VideoPlaybackService : IDisposable
{
    private readonly LibVlcProvider _provider;
    private Media? _media;
    private bool _startPausedMode;

    public MediaPlayer Player { get; }

    public VideoPlaybackService(LibVlcProvider provider)
    {
        _provider = provider;
        Player = new MediaPlayer(_provider.LibVlc)
        {
            EnableKeyInput = false,   // ввод обрабатываем сами через WPF-оверлей
            EnableMouseInput = false
        };
        Player.Playing += OnPlaying;
    }

    /// <summary>Задать видео (resume через :start-time). Воспроизведение запускается отдельно через Play().</summary>
    public void Load(string path, long startMs = 0)
    {
        var old = _media;

        // FromType.FromPath передаёт локальный путь напрямую — корректно для путей
        // со спецсимволами (#, %), на которых new Uri(path) бросает UriFormatException.
        var media = new Media(_provider.LibVlc, path, FromType.FromPath);

        _media = media;

        // start-paused декодирует первый кадр до показа, убирая белое мерцание
        // при переключении видео (особенно через SwitchTo).
        _startPausedMode = true;
        _media.AddOption(":start-paused");

        if (startMs > 1000)
            _media.AddOption($":start-time={startMs / 1000.0:0.###}");
        Player.Media = _media;
        old?.Dispose();
    }

    private void OnPlaying(object? sender, EventArgs e)
    {
        // start-paused остановится на первом кадре; как только Playing пришло —
        // первый кадр отрисован и можно безопасно продолжать воспроизведение.
        if (_startPausedMode)
        {
            _startPausedMode = false;
            Resume();
        }
    }

    public void Play() => Player.Play();
    public void TogglePause() => Player.Pause();      // переключает play/pause
    public void Pause() => Player.SetPause(true);
    public void Resume() => Player.SetPause(false);
    public void Stop() => Player.Stop();

    /// <summary>
    /// Остановить воспроизведение и явно освободить Media, чтобы LibVLC закрыл
    /// файловый handle. Используется перед удалением текущего видео.
    /// </summary>
    public void StopAndRelease()
    {
        try { Player.Stop(); } catch { }
        try { Player.Media = null; } catch { }
        _media?.Dispose();
        _media = null;
    }

    public long Time { get => Player.Time; set => Player.Time = value; }
    public long Length => Player.Length;

    /// <summary>Частота кадров видео (0, если ещё неизвестна).</summary>
    public float Fps => Player.Fps;

    /// <summary>Шаг на один кадр вперёд (LibVLC автоматически ставит на паузу).</summary>
    public void NextFrame() => Player.NextFrame();

    /// <summary>Скорость воспроизведения (0.25–2.0).</summary>
    public float Rate
    {
        get => Player.Rate;
        set => Player.SetRate(value);
    }

    /// <summary>Громкость 0–300 %. Выше 100 % — программное усиление (boost) средствами LibVLC.</summary>
    public int Volume
    {
        get => Player.Volume;
        set => Player.Volume = Math.Clamp(value, 0, MaxVolume);
    }

    /// <summary>Максимальная громкость с учётом усиления.</summary>
    public const int MaxVolume = 300;

    public bool Mute
    {
        get => Player.Mute;
        set => Player.Mute = value;
    }

    public bool IsPlaying => Player.IsPlaying;

    // --- Аудиодорожки и субтитры (доступны после старта воспроизведения) ---

    public TrackDescription[] AudioTracks => Player.AudioTrackDescription;
    public TrackDescription[] SubtitleTracks => Player.SpuDescription;
    public int CurrentAudioTrack => Player.AudioTrack;
    public int CurrentSubtitle => Player.Spu;

    public bool SetAudioTrack(int id) => Player.SetAudioTrack(id);
    public bool SetSubtitle(int id) => Player.SetSpu(id);

    /// <summary>Подключить внешний файл субтитров (.srt/.ass/…) и при необходимости включить его.</summary>
    public bool AddSubtitleFile(string path, bool select)
        => Player.AddSlave(MediaSlaveType.Subtitle, ToFileUri(path), select);

    /// <summary>Построить корректный file:// URI, экранируя # и % (на них new Uri бросает UriFormatException).</summary>
    internal static string ToFileUri(string path) => Prosmotr.Infrastructure.PathUri.ToUri(path).AbsoluteUri;

    /// <summary>Сохранить текущий кадр в файл (исходный размер). true — запрос принят.</summary>
    public bool TakeSnapshot(string path) => Player.TakeSnapshot(0, path, 0, 0);

    public void Dispose()
    {
        Player.Playing -= OnPlaying;
        try { Player.Stop(); } catch { }
        try { Player.Media = null; } catch { }
        _media?.Dispose();
        _media = null;
        Player.Dispose();
    }
}
