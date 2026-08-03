using System.Runtime.InteropServices;
using System.Text;
using LibVLCSharp.Shared;

namespace Prosmotr.Services;

/// <summary>Декодированный кадр превью в памяти. Формат B,G,R,A (RV32), 4 байта/пиксель.
/// Stride (pitch) может быть больше Width*4 — выравнивание кратно 32 (требование libvlc).</summary>
public sealed record PreviewFrame(byte[] Data, int Width, int Height, int Stride);

/// <summary>
/// Второй «скрытый» декодер для превью кадра при наведении на таймлайн. Отдельный MediaPlayer
/// выводит видео в память (SetVideoFormatCallbacks + SetVideoCallbacks) — без окна/HWND и без
/// временных файлов; основной плеер не трогается. Кадры масштабируются до ≤320px по ширине
/// с сохранением пропорций (в format-колбеке).
/// </summary>
public sealed class VideoFramePreviewService : IDisposable
{
    private const uint MaxPreviewWidth = 320;
    private const uint AlignUnit = 32;
    private static readonly byte[] RgbaChroma = Encoding.ASCII.GetBytes("RV32"); // B,G,R,A (little-endian)

    private readonly LibVLC _libVlc;
    private MediaPlayer? _player;
    private Media? _media;
    private string? _path;
    private bool _primed;

    private readonly object _sync = new();
    private byte[]? _buffer;
    private GCHandle _bufferHandle;
    private uint _pitch;   // выровненная ширина строки, байт
    private uint _width;   // реальная ширина кадра после масштаба
    private uint _height;  // реальная высота кадра
    private TaskCompletionSource<bool>? _frameTcs;
    private PreviewFrame? _lastFrame;

    public VideoFramePreviewService(LibVLC libVlc) => _libVlc = libVlc;

    /// <summary>Запросить кадр на позиции ms. null — кадр не получен (таймаут/ошибка/отмена).</summary>
    public async Task<PreviewFrame?> GetFrameAsync(long ms, CancellationToken ct)
    {
        if (!EnsureReady() || _player == null) return null;

        lock (_sync) { _player.Time = ms; }
        var frame = await WaitForFrameAsync(ct).ConfigureAwait(false);
        if (frame != null) return frame;

        // Fallback: на некоторых кодек/контейнерах paused-seek не перерисовывает кадр.
        // Короткий Play→Pause форсирует отрисовку (звука нет — :no-audio).
        // ВАЖНО: Play вызываем ВНЕ lock (_sync) — иначе vout-поток, блокирующийся на том же
        // lock в колбеках, не сможет продвинуться, пока Play ждёт pipeline.
        bool start = false;
        lock (_sync) { if (_player != null && !_player.IsPlaying) start = true; }
        if (!start) return null;
        _player!.Play();
        frame = await WaitForFrameAsync(ct).ConfigureAwait(false);
        lock (_sync)
        {
            try { _player?.SetPause(true); } catch { /* ignore */ }
        }
        return frame;
    }

    /// <summary>Сменить файл. Idempotent: ничего не делает, если путь тот же и медиа живое.</summary>
    public void Reset(string path)
    {
        lock (_sync)
        {
            if (string.Equals(_path, path, StringComparison.OrdinalIgnoreCase) && _media != null) return;
        }
        ReleaseMedia();
        lock (_sync) { _path = path; }
    }

    /// <summary>Освободить медиа (закрыть файловый handle). Вызывается при удалении файла / смене видео.
    /// Stop/Dispose выполняются ВНЕ lock (_sync): vout-поток блокируется на этом lock в колбеках
    /// (OnLock/OnDisplay/OnFormat), а Stop ждёт завершения vout — иначе взаимная блокировка.</summary>
    public void ReleaseMedia()
    {
        MediaPlayer? p;
        Media? m;
        lock (_sync)
        {
            _primed = false;
            _frameTcs?.TrySetResult(false);
            _frameTcs = null;
            _lastFrame = null;
            p = _player;
            m = _media;
            _media = null;
        }
        try { p?.Stop(); } catch { /* ignore */ }
        try { if (p != null) p.Media = null; } catch { /* ignore */ }
        m?.Dispose();
    }

    public void Dispose()
    {
        ReleaseMedia();
        MediaPlayer? p;
        lock (_sync)
        {
            if (_bufferHandle.IsAllocated) _bufferHandle.Free();
            _buffer = null;
            p = _player;
            _player = null;
        }
        p?.Dispose(); // вне lock — как в ReleaseMedia
    }

    // --- Внутреннее ---

    private bool EnsureReady()
    {
        bool needPrime = false;
        MediaPlayer? p = null;
        Media? m = null;
        lock (_sync)
        {
            if (_player == null)
            {
                _player = new MediaPlayer(_libVlc)
                {
                    EnableKeyInput = false,
                    EnableMouseInput = false,
                    EnableHardwareDecoding = false
                };
                _player.SetVideoFormatCallbacks(OnFormat, null);
                _player.SetVideoCallbacks(OnLock, null, OnDisplay);
            }
            if (_media == null && _path != null)
            {
                // FromType.FromPath — корректно для путей со спецсимволами (#, %), как в VideoPlaybackService.Load.
                _media = new Media(_libVlc, _path, FromType.FromPath);
                _media.AddOption(":no-audio");
            }
            if (_media == null) return false;
            if (!_primed)
            {
                _primed = true;
                needPrime = true;
                p = _player;
                m = _media;
            }
        }
        if (needPrime && p != null && m != null)
        {
            p.Play(m); // вне lock — vout-поток не должен блокироваться на _sync, пока Play поднимает pipeline
            _ = DelayPauseAsync();
        }
        return true;
    }

    /// <summary>Через ~200 мс после старта пауза: превью-плеер живёт в состоянии паузы, seek перерисовывает кадр.</summary>
    private async Task DelayPauseAsync()
    {
        try { await Task.Delay(200).ConfigureAwait(false); } catch { return; }
        lock (_sync)
        {
            try { _player?.SetPause(true); } catch { /* ignore */ }
        }
    }

    private async Task<PreviewFrame?> WaitForFrameAsync(CancellationToken ct)
    {
        TaskCompletionSource<bool> tcs;
        lock (_sync)
        {
            _frameTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            tcs = _frameTcs;
        }
        try
        {
            var delay = Task.Delay(2000, ct);
            var done = await Task.WhenAny(tcs.Task, delay).ConfigureAwait(false);
            if (done != tcs.Task) return null; // таймаут или отмена
            lock (_sync) { return _lastFrame; }
        }
        catch (OperationCanceledException) { return null; }
    }

    // --- Колбеки vout (поток vout, НЕ UI) ---

    /// <summary>Формат вывода: форсируем RV32 (RGBA), масштабируем до ≤320px по ширине с сохранением
    /// пропорций, выравниваем pitch/lines кратно 32. Вызывается до первого кадра.</summary>
    private uint OnFormat(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height,
        ref uint pitches, ref uint lines)
    {
        try { Marshal.Copy(RgbaChroma, 0, chroma, 4); } catch { /* ignore */ }

        if (width > MaxPreviewWidth)
        {
            height = (uint)Math.Max(1, Math.Round(height * (double)MaxPreviewWidth / width));
            width = MaxPreviewWidth;
        }
        pitches = Align(width * 4);
        lines = Align(height);

        lock (_sync)
        {
            var size = (int)(pitches * lines);
            if (_buffer == null || _buffer.Length != size)
            {
                if (_bufferHandle.IsAllocated) _bufferHandle.Free();
                _buffer = new byte[size];
                _bufferHandle = GCHandle.Alloc(_buffer, GCHandleType.Pinned);
            }
            _pitch = pitches;
            _width = width;
            _height = height;
        }
        return 1; // число буферов-картинок, которые готов отдать lock-колбек (0 = отказ, vmem не запустится)
    }

    private IntPtr OnLock(IntPtr opaque, IntPtr planes)
    {
        lock (_sync)
        {
            if (_bufferHandle.IsAllocated)
                Marshal.WriteIntPtr(planes, _bufferHandle.AddrOfPinnedObject());
        }
        return IntPtr.Zero;
    }

    private void OnDisplay(IntPtr opaque, IntPtr picture)
    {
        lock (_sync)
        {
            if (_buffer == null || !_bufferHandle.IsAllocated) return;
            var copy = new byte[_buffer.Length];
            Buffer.BlockCopy(_buffer, 0, copy, 0, _buffer.Length);
            _lastFrame = new PreviewFrame(copy, (int)_width, (int)_height, (int)_pitch);
            var tcs = _frameTcs;
            _frameTcs = null;
            tcs?.TrySetResult(true);
        }
    }

    private static uint Align(uint size) => size % AlignUnit == 0 ? size : ((size / AlignUnit) + 1) * AlignUnit;
}
