# Превью кадра при наведении на таймлайн — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** При наведении на таймлайн видео показывать миниатюру кадра на позиции под курсором (как YouTube), не прерывая воспроизведение; опциональный режим «пауза при наведении».

**Architecture:** Второй скрытый `MediaPlayer` (тот же `LibVLC`) выводит видео в память через `SetVideoFormatCallbacks` + `SetVideoCallbacks` (RGBA ≤320px, без HWND/temp-файлов). `VideoViewerViewModel` владеет экстрактором и методами `RequestPreviewFrameAsync` / `PauseForPreview` / `ResumeFromPreview`; `VideoViewerView` (code-behind) обрабатывает ховер/drag по слайдеру, позиционирует панель превью в оверлее и дросселирует запросы кадров.

**Tech Stack:** WPF .NET 8, LibVLCSharp 3.9.7.1, CommunityToolkit.Mvvm, xUnit.

## Global Constraints

- **Коммиты — ТОЛЬКО по явному указанию пользователя** (правило проекта AGENTS.md). Каждый таск заканчивается проверкой, а НЕ коммитом.
- Сборка/проверка: `dotnet build src\Prosmotr\Prosmotr.csproj -c Debug`; тесты: `dotnet test tests\Prosmotr.Tests\Prosmotr.Tests.csproj`. Публикация (только в финальном таске): `dotnet publish src\Prosmotr\Prosmotr.csproj -c Release -o app` — после закрытия процессов `Prosmotr`.
- Платформа: x64 только (`PlatformTarget=x64`); **никаких** single-file/trimming.
- Спека: `docs/superpowers/specs/2026-08-03-timeline-preview-design.md`.
- Проект использует ImplicitUsings + Nullable. Комментарии — на русском, «почему», а не «что».
- `EnableHardwareDecoding = false` — обязательно и для превью-плеера (консистентно с основным).
- Юнит-тесты не грузят LibVLC/WPF — только чистая логика. UI/VLC проверяется вручную.

---

### Task 1: Хелпер `TimelineMath` + юнит-тесты

**Files:**

- Create: `src/Prosmotr/Infrastructure/TimelineMath.cs`
- Test: `tests/Prosmotr.Tests/TimelineMathTests.cs`

**Interfaces:**

- Consumes: ничего.
- Produces: `public static class TimelineMath { public static double MapSliderXToMs(double x, double width, double lengthMs) }` — пропорциональное отображение X-координаты мыши на слайдере (DIP) в миллисекунды видео; clamp `[0, lengthMs]`; при `width <= 0 || lengthMs <= 0` → 0.

- [ ] **Step 1: Написать падающий тест**

```csharp
using Prosmotr.Infrastructure;
using Xunit;

namespace Prosmotr.Tests;

/// <summary>Отображение X-координаты мыши на слайдере таймлайна в миллисекунды видео.</summary>
public sealed class TimelineMathTests
{
    [Fact] public void Map_LeftEdge_IsZero() => Assert.Equal(0, TimelineMath.MapSliderXToMs(0, 100, 10_000));
    [Fact] public void Map_Middle_IsHalf() => Assert.Equal(5_000, TimelineMath.MapSliderXToMs(50, 100, 10_000));
    [Fact] public void Map_Proportional() => Assert.Equal(2_500, TimelineMath.MapSliderXToMs(25, 100, 10_000));
    [Fact] public void Map_RightEdge_IsLength() => Assert.Equal(10_000, TimelineMath.MapSliderXToMs(100, 100, 10_000));
    [Fact] public void Map_ClampsBeyondWidth_ToLength() => Assert.Equal(10_000, TimelineMath.MapSliderXToMs(150, 100, 10_000));
    [Fact] public void Map_ClampsNegativeX_ToZero() => Assert.Equal(0, TimelineMath.MapSliderXToMs(-10, 100, 10_000));
    [Fact] public void Map_ZeroWidth_ReturnsZero() => Assert.Equal(0, TimelineMath.MapSliderXToMs(50, 0, 10_000));
    [Fact] public void Map_ZeroLength_ReturnsZero() => Assert.Equal(0, TimelineMath.MapSliderXToMs(50, 100, 0));
}
```

- [ ] **Step 2: Убедиться, что тест падает (нет класса)**

Run: `dotnet test tests\Prosmotr.Tests\Prosmotr.Tests.csproj --filter "FullyQualifiedName~TimelineMathTests"`
Expected: FAIL (CS0246 — тип `TimelineMath` не найден).

- [ ] **Step 3: Реализовать хелпер**

```csharp
namespace Prosmotr.Infrastructure;

/// <summary>Позиция таймлайна: X-координата мыши на слайдере → миллисекунды видео.</summary>
public static class TimelineMath
{
    /// <summary>Пропорционально отобразить позицию мыши (в DIP) в время (мс).
    /// x — смещение от левого края слайдера; width — ActualWidth слайдера (0 → 0);
    /// lengthMs — длительность видео (0 → 0). Результат клампится в [0, lengthMs].</summary>
    public static double MapSliderXToMs(double x, double width, double lengthMs)
    {
        if (width <= 0 || lengthMs <= 0) return 0;
        var ratio = Math.Clamp(x / width, 0.0, 1.0);
        return ratio * lengthMs;
    }
}
```

- [ ] **Step 4: Прогнать тесты**

Run: `dotnet test tests\Prosmotr.Tests\Prosmotr.Tests.csproj --filter "FullyQualifiedName~TimelineMathTests"`
Expected: PASS (8 тестов). Затем прогнать всю пачку: `dotnet test tests\Prosmotr.Tests\Prosmotr.Tests.csproj` — все зелёные.

- [ ] **Step 5: Checkpoint** — сборка всего решения: `dotnet build src\Prosmotr\Prosmotr.csproj -c Debug` (0 ошибок). НЕ коммитить.

---

### Task 2: Настройки `ShowTimelinePreview` и `TimelinePreviewPauseVideo`

**Files:**

- Modify: `src/Prosmotr/Models/AppSettings.cs`
- Modify: `src/Prosmotr/ViewModels/SettingsViewModel.cs`
- Modify: `src/Prosmotr/Views/SettingsWindow.xaml`

**Interfaces:**

- Consumes: существующие паттерны `[ObservableProperty]` + `Commit(immediate:)` + `Card`/`ToggleSwitch` в настройках.
- Produces: `AppSettings.ShowTimelinePreview` (bool, default `true`), `AppSettings.TimelinePreviewPauseVideo` (bool, default `false`); свойства VM `ShowTimelinePreview`, `TimelinePreviewPauseVideo` (TwoWay); обе карточки в разделе «Видео» окна настроек; live-применение через `Commit(immediate: true)`.

- [ ] **Step 1: `AppSettings.cs`** — в секцию `// --- Видео ---`, сразу после `MiniTimelineThresholdMinutes`:

```csharp
/// <summary>Показывать превью кадра при наведении на таймлайн видео.</summary>
public bool ShowTimelinePreview { get; set; } = true;
/// <summary>Ставить видео на паузу при наведении на таймлайн (режим скраббинга).</summary>
public bool TimelinePreviewPauseVideo { get; set; } = false;
```

- [ ] **Step 2: `SettingsViewModel.cs`** — добавить свойства, загрузку, запись, обработчики.

Рядом с существующими `[ObservableProperty]` для мини-таймлайна (`_showMiniTimeline` и т.п.):

```csharp
[ObservableProperty] private bool _showTimelinePreview;
[ObservableProperty] private bool _timelinePreviewPauseVideo;
```

В `LoadFromSettings()` рядом с `MiniTimelineThresholdMinutes = s.MiniTimelineThresholdMinutes;`:

```csharp
ShowTimelinePreview = s.ShowTimelinePreview;
TimelinePreviewPauseVideo = s.TimelinePreviewPauseVideo;
```

В `Commit(bool immediate = false)` рядом с `s.MiniTimelineThresholdMinutes = ...`:

```csharp
s.ShowTimelinePreview = ShowTimelinePreview;
s.TimelinePreviewPauseVideo = TimelinePreviewPauseVideo;
```

Рядом с `OnShowMiniTimelineChanged`:

```csharp
partial void OnShowTimelinePreviewChanged(bool value) => Commit(immediate: true);
partial void OnTimelinePreviewPauseVideoChanged(bool value) => Commit(immediate: true);
```

- [ ] **Step 3: `SettingsWindow.xaml`** — две карточки в разделе «Видео», ПОСЛЕ карточки «Показывать мини-таймлайн для видео до» и ПЕРЕД `<!-- УДАЛЕНИЕ -->`:

```xml
<Border Style="{StaticResource Card}">
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*" />
            <ColumnDefinition Width="Auto" />
        </Grid.ColumnDefinitions>
        <StackPanel>
            <TextBlock Text="Превью при наведении на таймлайн" />
            <TextBlock Text="Показывать кадр видео под курсором при наведении на таймлайн (как YouTube)"
                       Opacity="0.6" FontSize="12" TextWrapping="Wrap" />
        </StackPanel>
        <ui:ToggleSwitch Grid.Column="1" IsChecked="{Binding ShowTimelinePreview, Mode=TwoWay}" />
    </Grid>
</Border>

<Border Style="{StaticResource Card}">
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*" />
            <ColumnDefinition Width="Auto" />
        </Grid.ColumnDefinitions>
        <StackPanel>
            <TextBlock Text="Пауза при наведении на таймлайн" />
            <TextBlock Text="Видео ставится на паузу при наведении и возобновляется после ухода мыши"
                       Opacity="0.6" FontSize="12" TextWrapping="Wrap" />
        </StackPanel>
        <ui:ToggleSwitch Grid.Column="1" IsChecked="{Binding TimelinePreviewPauseVideo, Mode=TwoWay}"
                         IsEnabled="{Binding ShowTimelinePreview}" />
    </Grid>
</Border>
```

- [ ] **Step 4: Проверка**

Run: `dotnet build src\Prosmotr\Prosmotr.csproj -c Debug` — 0 ошибок; `dotnet test tests\Prosmotr.Tests\Prosmotr.Tests.csproj` — зелёные (настройки валидируются без изменений: bool-свойства без `[Range]`).

- [ ] **Step 5: Checkpoint** — НЕ коммитить.

---

### Task 3: Сервис захвата кадра `VideoFramePreviewService`

**Files:**

- Create: `src/Prosmotr/Services/VideoFramePreviewService.cs`

**Interfaces:**

- Consumes: `LibVLC` из `LibVlcProvider.LibVlc`; LibVLCSharp 3.9.7.1 API (`MediaPlayer.SetVideoFormatCallbacks`, `SetVideoCallbacks`, `Play(Media)`, `SetPause`, `Time`, `Media(path, FromType.FromPath)` + `AddOption(":no-audio")`).
- Produces:
  - `public sealed record PreviewFrame(byte[] Data, int Width, int Height, int Stride)`
  - `public sealed class VideoFramePreviewService : IDisposable` с методами:
    - `public async Task<PreviewFrame?> GetFrameAsync(long ms, CancellationToken ct)`
    - `public void Reset(string path)` — сменить файл (idempotent)
    - `public void ReleaseMedia()` — освободить медиа/handle (удаление, смена видео)
    - `public void Dispose()`

**Ключевые факты сигнатур LibVLCSharp 3.9.7.1 (проверено по исходникам тега 3.9.7.1):**

- `public delegate IntPtr LibVLCVideoLockCb(IntPtr opaque, IntPtr planes)` — буфер задаётся через `Marshal.WriteIntPtr(planes, ptr)`.
- `public delegate void LibVLCVideoDisplayCb(IntPtr opaque, IntPtr picture)`.
- `public delegate uint LibVLCVideoFormatCb(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height, ref uint pitches, ref uint lines)`.
- `SetVideoFormatCallbacks` и `SetVideoCallbacks` взаимоисключаемы с `SetVideoFormat(string, uint, uint, uint)` — используем **только** callbacks-вариант.

- [ ] **Step 1: Создать файл** (полный код):

```csharp
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
        _player.Play();
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
        PreviewFrame? frame = null;
        lock (_sync)
        {
            if (_buffer == null || !_bufferHandle.IsAllocated) return;
            var copy = new byte[_buffer.Length];
            Buffer.BlockCopy(_buffer, 0, copy, 0, _buffer.Length);
            frame = new PreviewFrame(copy, (int)_width, (int)_height, (int)_pitch);
            _lastFrame = frame;
            var tcs = _frameTcs;
            _frameTcs = null;
            tcs?.TrySetResult(true);
        }
    }

    private static uint Align(uint size) => size % AlignUnit == 0 ? size : ((size / AlignUnit) + 1) * AlignUnit;
}
```

- [ ] **Step 2: Сборка**

Run: `dotnet build src\Prosmotr\Prosmotr.csproj -c Debug`
Expected: 0 ошибок, 0 предупреждений по новому файлу (класс пока никем не используется — ок).

- [ ] **Step 3: Checkpoint** — НЕ коммитить. (Нативную работу проверим в Task 4/5 вручную.)

---

### Task 4: `VideoViewerViewModel` — владение экстрактором, превью, пауза

**Files:**

- Modify: `src/Prosmotr/ViewModels/VideoViewerViewModel.cs`

**Interfaces:**

- Consumes: Task 3 (`VideoFramePreviewService`, `PreviewFrame`); Task 2 (`AppSettings.ShowTimelinePreview`, `.TimelinePreviewPauseVideo`); существующий `LibVlcProvider` из конструктора.
- Produces (для Task 5):
  - `public bool TimelinePreviewEnabled { get; }` — из настроек, live (PropertyChanged через `OnSettingsChanged`);
  - `public bool PauseVideoOnHover { get; }` — из настроек, live;
  - `public Task<System.Windows.Media.Imaging.BitmapSource?> RequestPreviewFrameAsync(long ms)` — «только последний запрос важен», отмена старого CTS, конвертация в `BitmapSource` на UI-потоке, `Freeze()`;
  - `public void PauseForPreview()` / `public void ResumeFromPreview()` — режим паузы с восстановлением «играло ли до этого».

- [ ] **Step 1: Поля и конструктор**

Добавить usings (в начало файла, рядом с существующими):

```csharp
using System.Windows.Media.Imaging;
```

Добавить поле (рядом с `private readonly VideoPlaybackService _playback;`):

```csharp
private readonly LibVlcProvider _provider;
```

В конструкторе (сейчас параметр `LibVlcProvider provider` не сохраняется) добавить в начало тела:

```csharp
_provider = provider;
```

Добавить поля (рядом с `private bool _wasPlayingBeforePreview;` в группе полей):

```csharp
// Превью при наведении на таймлайн: второй скрытый декодер + «только последний запрос важен».
private VideoFramePreviewService? _preview;
private CancellationTokenSource? _previewCts;
private int _previewGen;
private bool _wasPlayingBeforePreview;
```

- [ ] **Step 2: Публичные свойства и методы** (добавить после `UpdateCanShowMiniTimeline`, перед `OnUi`):

```csharp
/// <summary>Включено ли превью при наведении на таймлайн (настройка, live).</summary>
public bool TimelinePreviewEnabled => _settings.Settings.ShowTimelinePreview;

/// <summary>Ставить ли видео на паузу при наведении на таймлайн (настройка, live).</summary>
public bool PauseVideoOnHover => _settings.Settings.TimelinePreviewPauseVideo;

/// <summary>Запросить кадр превью на позиции ms. Возвращает замороженный BitmapSource или null.
/// Побеждает последний запрос: предыдущий отменяется (поколение _previewGen). Вызывается с UI-потока.</summary>
public async Task<BitmapSource?> RequestPreviewFrameAsync(long ms)
{
    if (_disposed) return null;
    _previewCts?.Cancel();
    _previewCts?.Dispose();
    _previewCts = new CancellationTokenSource();
    var gen = ++_previewGen;
    try
    {
        _preview ??= new VideoFramePreviewService(_provider.LibVlc);
        _preview.Reset(Item.FullPath);
        var frame = await _preview.GetFrameAsync(ms, _previewCts.Token);
        if (frame == null || gen != _previewGen || _disposed) return null;
        // BitmapSource обязан создаваться на UI-потоке: await без ConfigureAwait(false) продолжает в UI-контексте.
        var bmp = BitmapSource.Create(frame.Width, frame.Height, 96, 96,
            MediaRendering.PixelFormats.Bgra32, null, frame.Data, frame.Stride);
        bmp.Freeze();
        return bmp;
    }
    catch (OperationCanceledException) { return null; }
    catch { return null; }
}

/// <summary>Наведение на таймлайн в режиме «пауза»: запомнить состояние и поставить на паузу.</summary>
public void PauseForPreview()
{
    if (_disposed || _wasPlayingBeforePreview || IsEnded || IsBuffering) return;
    _wasPlayingBeforePreview = _playback.IsPlaying;
    if (_wasPlayingBeforePreview) _playback.Pause();
}

/// <summary>Уход мыши с таймлайна: возобновить, если до наведения видео играло.</summary>
public void ResumeFromPreview()
{
    if (_disposed || !_wasPlayingBeforePreview) return;
    _wasPlayingBeforePreview = false;
    if (!IsEnded && !IsBuffering && !_playback.IsPlaying) _playback.Play();
}
```

- [ ] **Step 3: `OnSettingsChanged`** — заменить существующий метод (сейчас `OnUi(UpdateCanShowMiniTimeline);`) на:

```csharp
private void OnSettingsChanged(object? sender, EventArgs e)
{
    OnUi(() =>
    {
        UpdateCanShowMiniTimeline();
        // Live-применение превью-настроек: View подписан на эти свойства.
        OnPropertyChanged(nameof(TimelinePreviewEnabled));
        OnPropertyChanged(nameof(PauseVideoOnHover));
    });
}
```

- [ ] **Step 4: `SwitchTo`** — после `_playback.StopAndRelease();` добавить:

```csharp
_preview?.ReleaseMedia();        // старый файл: закрыть handle; новый поднимется лениво при наведении
_wasPlayingBeforePreview = false;
```

- [ ] **Step 5: `StopAndRelease`** — заменить тело на:

```csharp
public void StopAndRelease()
{
    if (_disposed) return;
    _playback.StopAndRelease();
    _preview?.ReleaseMedia(); // превью-плеер тоже держит файловый handle — освобождаем до IFileOperation (удаление)
}
```

- [ ] **Step 6: `Dispose`** — перед `_playback.Dispose();` добавить:

```csharp
_preview?.Dispose();
_preview = null;
_previewCts?.Cancel();
_previewCts?.Dispose();
_previewCts = null;
```

- [ ] **Step 7: Сборка**

Run: `dotnet build src\Prosmotr\Prosmotr.csproj -c Debug`
Expected: 0 ошибок. Примечание: `BitmapSource` — из добавленного `using System.Windows.Media.Imaging;`; `PixelFormats` — это `System.Windows.Media.PixelFormats`, и в этом файле оно доступно через существующий alias `using MediaRendering = System.Windows.Media;`, поэтому в коде используется `MediaRendering.PixelFormats.Bgra32` (плоский `using System.Windows.Media;` не добавляем — не нужен).

- [ ] **Step 8: Checkpoint** — НЕ коммитить.

---

### Task 5: `VideoViewerView` — панель превью и обработка ховера/drag

**Files:**

- Modify: `src/Prosmotr/Views/VideoViewerView.xaml`
- Modify: `src/Prosmotr/Views/VideoViewerView.xaml.cs`

**Interfaces:**

- Consumes: Task 1 (`TimelineMath.MapSliderXToMs`), Task 4 (`_vm.TimelinePreviewEnabled`, `_vm.PauseVideoOnHover`, `_vm.RequestPreviewFrameAsync(long)`, `_vm.PauseForPreview()`, `_vm.ResumeFromPreview()`), существующие `_controlsShown`/`UpdateChromeVisibility`/`_hideTimer` паттерны.
- Produces: панель `PreviewPanel` (Image + метка времени), обработчики слайдера (`MouseEnter/MouseMove/MouseLeave`), серийная «погоня за курсором» (семплер 150 мс + `PumpPreviewFrame`, один запрос за раз), таймер паузы 250 мс, скрытие в `UpdateChromeVisibility` и при смене контента.

- [ ] **Step 1: XAML — панель превью**

В `Overlay` (Grid), ПОСЛЕ закрывающего `</Border>` панели `ControlBar` и ПЕРЕД комментарием `<!-- Мини-таймлайн ... -->` вставить:

```xml
<!-- Превью кадра при наведении на таймлайн (как YouTube): позиционируется code-behind'ом
     над слайдером; видимостью управляет он же. IsHitTestVisible=False — не мешаем кликам. -->
<Border x:Name="PreviewPanel"
        Grid.Row="0"
        HorizontalAlignment="Left"
        VerticalAlignment="Bottom"
        Width="328" Height="208"
        Background="#E61A1A1A"
        BorderBrush="#66FFFFFF"
        BorderThickness="1"
        CornerRadius="6"
        Padding="4"
        IsHitTestVisible="False"
        Visibility="Collapsed">
    <Border.Effect>
        <DropShadowEffect BlurRadius="8" ShadowDepth="1" Opacity="0.4" />
    </Border.Effect>
    <StackPanel>
        <Image x:Name="PreviewImage" Width="320" Height="180" Stretch="Uniform" SnapsToDevicePixels="True" />
        <TextBlock x:Name="PreviewTime" Foreground="White" FontSize="11" HorizontalAlignment="Center"
                   Margin="0,3,0,1" />
    </StackPanel>
</Border>
```

- [ ] **Step 2: Code-behind — поля и конструктор**

Добавить usings (в начало файла):

```csharp
using System.Globalization;
using Prosmotr.Converters;
```

Добавить поля (рядом с `_speedMenuClosedAt` и т.п.):

```csharp
private readonly DispatcherTimer _previewThrottle;  // дебаунс запросов кадра превью
private readonly DispatcherTimer _pauseHoverTimer;  // задержка паузы при наведении (режим «пауза»)
private bool _previewVisible;
private double _lastHoverMs;
private static readonly MillisecondsToTimeConverter MsToTime = new();
```

В конструкторе, рядом с `_seekCooldown` init и подписками `PositionSlider.*` (после `PositionSlider.ValueChanged += ...` и AddHandler для Thumb, перед `Overlay.ContextMenu = ...`):

```csharp
// Превью при наведении: дросселируем запросы кадров, чтобы не спамить второй декодер.
_previewThrottle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
_previewThrottle.Tick += OnPreviewThrottleTick;
// Режим «пауза при наведении»: случайное пересечение курсора не должно ставить паузу — ждём 250 мс.
_pauseHoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
_pauseHoverTimer.Tick += OnPauseHoverTick;

PositionSlider.MouseEnter += OnSliderMouseEnter;
PositionSlider.MouseLeave += OnSliderMouseLeave;
PositionSlider.MouseMove += OnSliderMouseMove;
```

- [ ] **Step 3: Code-behind — обработчики** (добавить после `OnSeekCooldownTick`):

```csharp
// --- Превью кадра при наведении на таймлайн (как YouTube) ---

/// <summary>Можно ли показывать превью прямо сейчас (настройка + состояние видео).</summary>
private bool CanPreview()
    => _vm != null
       && _vm.TimelinePreviewEnabled
       && _vm.LengthMs > 0
       && !_vm.HasError
       && _vm.IsBuffering != true;

private void OnSliderMouseEnter(object sender, MouseEventArgs e)
{
    if (!CanPreview()) return;
    ShowPreviewPanel();
    if (_vm?.PauseVideoOnHover == true)
    {
        _pauseHoverTimer.Stop();
        _pauseHoverTimer.Start();
    }
}

private void OnSliderMouseMove(object sender, MouseEventArgs e)
{
    if (!CanPreview() || _vm == null) return;
    var pos = e.GetPosition(PositionSlider);
    _lastHoverMs = TimelineMath.MapSliderXToMs(pos.X, PositionSlider.ActualWidth, _vm.LengthMs);
    UpdatePreviewPosition();
    _previewThrottle.Stop();
    _previewThrottle.Start();
}

private void OnSliderMouseLeave(object sender, MouseEventArgs e)
{
    _previewThrottle.Stop();
    _pauseHoverTimer.Stop();
    _vm?.ResumeFromPreview();
    HidePreviewPanel();
}

private async void OnPreviewThrottleTick(object? sender, EventArgs e)
{
    _previewThrottle.Stop();
    if (!_previewVisible || _vm == null) return;
    var ms = _lastHoverMs;
    var frame = await _vm.RequestPreviewFrameAsync((long)ms);
    // Пока кадр декодировался, курсор мог уйти или уехать дальше — не показываем устаревший.
    if (frame == null || !_previewVisible || _lastHoverMs != ms) return;
    PreviewImage.Source = frame;
}

private void OnPauseHoverTick(object? sender, EventArgs e)
{
    _pauseHoverTimer.Stop();
    _vm?.PauseForPreview();
}

private void ShowPreviewPanel()
{
    _previewVisible = true;
    PreviewPanel.Visibility = Visibility.Visible;
    UpdatePreviewPosition();
    PreviewPanel.UpdateLayout(); // чтобы ActualWidth/Height были известны для позиционирования
    UpdatePreviewPosition();
}

private void HidePreviewPanel()
{
    _previewVisible = false;
    _previewThrottle.Stop();
    if (PreviewPanel != null)
        PreviewPanel.Visibility = Visibility.Collapsed;
}

/// <summary>Позиционирование панели над слайдером: следует за курсором по X, clamp по окну,
/// снизу отступ = высота панели управления + 10px. Обновляет метку времени.</summary>
private void UpdatePreviewPosition()
{
    if (PreviewPanel == null || _vm == null) return;
    var mouse = Mouse.GetPosition(Overlay);
    PreviewTime.Text = MsToTime.Convert(_lastHoverMs, null, null, CultureInfo.CurrentCulture)?.ToString() ?? string.Empty;
    var bottom = ControlBar.ActualHeight + 10;
    var w = Math.Max(1, PreviewPanel.ActualWidth);
    var left = mouse.X - w / 2;
    left = Math.Clamp(left, 4, Math.Max(4, Overlay.ActualWidth - w - 4));
    PreviewPanel.Margin = new Thickness(left, 0, 0, bottom);
}
```

- [ ] **Step 4: Скрытие превью в `UpdateChromeVisibility`** — в конец метода (после `UpdateSideNav(); UpdateInfo();`) добавить:

```csharp
// Превью при наведении живёт только вместе с панелью управления и корректным видео.
if (!show || !CanPreview())
    HidePreviewPanel();
```

- [ ] **Step 5: Реагирование на смену свойств VM** — в `OnVmPropertyChanged` в существующую ветку, вызывающую `UpdateChromeVisibility(); UpdateCover();` (сейчас условие: `IsBuffering || CanShowMiniTimeline || IsEnded || LengthMs`), добавить два имени:

```csharp
else if (e.PropertyName == nameof(VideoViewerViewModel.IsBuffering)
         || e.PropertyName == nameof(VideoViewerViewModel.CanShowMiniTimeline)
         || e.PropertyName == nameof(VideoViewerViewModel.IsEnded)
         || e.PropertyName == nameof(VideoViewerViewModel.LengthMs)
         || e.PropertyName == nameof(VideoViewerViewModel.TimelinePreviewEnabled)
         || e.PropertyName == nameof(VideoViewerViewModel.PauseVideoOnHover))
{
    UpdateChromeVisibility();
    UpdateCover();
}
```

- [ ] **Step 6: Очистка при смене контента и unload**

В `OnDataContextChanged`, ДО `Detach();` (первая строка метода сейчас `Detach();`) вставить:

```csharp
_vm?.ResumeFromPreview();
HidePreviewPanel();
```

В `OnUnloaded`, рядом с остановкой `_seekCooldown` и т.п., добавить:

```csharp
_previewThrottle.Stop();
_previewThrottle.Tick -= OnPreviewThrottleTick;
_pauseHoverTimer.Stop();
_pauseHoverTimer.Tick -= OnPauseHoverTick;
_vm?.ResumeFromPreview();
HidePreviewPanel();
```

- [ ] **Step 7: Сборка**

Run: `dotnet build src\Prosmotr\Prosmotr.csproj -c Debug`
Expected: 0 ошибок.

- [ ] **Step 8: Checkpoint** — НЕ коммитить.

---

### Task 6: AGENTS.md, полная проверка, публикация в `app\`

**Files:**

- Modify: `AGENTS.md`
- (нет кода)

**Interfaces:**

- Consumes: всё из Task 1–5.

- [ ] **Step 1: Обновить `AGENTS.md`**

1. Раздел 4 (карта каталогов): добавить `Services/VideoFramePreviewService.cs` — «второй скрытый декодер для превью кадра при наведении на таймлайн».
2. Раздел 5: новый подпункт (продолжить нумерацию, сейчас последний 5.32) — «Превью кадра при наведении на таймлайн»:
   - второй `MediaPlayer` + `SetVideoFormatCallbacks`/`SetVideoCallbacks` (RV32, ≤320px, выравнивание pitch/lines кратно 32; сигнатуры делегатов 3.9.7.1: lock `(IntPtr, IntPtr)`, format `(ref IntPtr, IntPtr, ref uint x4)`);
   - `:no-audio`, `EnableHardwareDecoding = false`; основной плеер не трогается;
   - дебаунс 120 мс в `VideoViewerView` (позже заменено на серийную погоню: семплер 150 мс + один запрос за раз, см. AGENTS.md §5.33 — дебаунс-вариант залипал на первом кадре при непрерывном движении), «только последний запрос важен» через `_previewGen`+CTS в `VideoViewerViewModel`; таймаут 2 с + fallback Play→Pause (некоторые кодеки не перерисовывают кадр на paused-seek);
   - превью-плеер живёт в паузе; `Time = ms` на paused-плеере перерисовывает кадр (как покадровый шаг);
   - `ReleaseMedia()` вызывается в `StopAndRelease`/`SwitchTo` — иначе IFileOperation не удалит файл (sharing violation);
   - настройки `ShowTimelinePreview` (default true), `TimelinePreviewPauseVideo` (default false), live-применение, `Commit(immediate: true)`; пауза при наведении с задержкой 250 мс и восстановлением «играло ли до»;
   - PiP и мини-таймлайн — превью не показывается (вне скоупа).
3. Раздел 7: добавить ручные сценарии проверки превью (ховер, drag, режим паузы, переворот у краёв, удаление видео при активном наведении, смена файла).

- [ ] **Step 2: Полный прогон**

```powershell
dotnet build src\Prosmotr\Prosmotr.csproj -c Debug
dotnet test tests\Prosmotr.Tests\Prosmotr.Tests.csproj
```

Expected: сборка 0/0, тесты все зелёные (104 + 8 новых).

- [ ] **Step 3: Публикация в `app\`** (по процессу AGENTS.md §3.2 — единый шаг: закрыть процессы, очистить, публиковать):

```powershell
Get-Process -Name "Prosmotr" -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item -Path "app" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "src\Prosmotr\bin","src\Prosmotr\obj" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "$env:TEMP\Prosmotr*" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "$env:TEMP\.NET*" -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish src\Prosmotr\Prosmotr.csproj -c Release -o app
```

- [ ] **Step 4: Ручная проверка** (запустить `app\Prosmotr.exe` с путём к видео):

1. Наведение на таймлайн — превью появляется над курсором, метка времени меняется, видео играет.
2. Перетаскивание бегунка — превью следует за позицией; отпускание перематывает.
3. Превью у правого края — не вылезает за окно (clamp).
4. «Пауза при наведении» вкл — наведение ставит на паузу, уход мыши возобновляет; если видео было на паузе — остаётся.
5. Тумблер «Превью при наведении» выкл — ничего не показывается.
6. Видео→видео — превью переключается на новый файл.
7. Удаление видео при активном наведении — удаление проходит без sharing violation.
8. Автоскрытие панели — превью скрывается вместе с панелью.
9. Полноэкранный режим — превью работает.
10. PiP — превью НЕ показывается (ожидаемо, вне скоупа).

- [ ] **Step 5: Checkpoint** — результат показать пользователю. Коммит — только по явному указанию.
