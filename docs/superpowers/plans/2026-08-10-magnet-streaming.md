# Магнет-стриминг в «Просмотре» — план реализации

> **Для агентных воркеров:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development (рекомендуется)
> или superpowers:executing-plans. Шаги — чекбоксы `- [ ]`.

**Goal:** Встроить в «Просмотр» просмотр фильмов по магнет-ссылке: вставил ссылку → движок
MonoTorrent качает → LibVLC играет по мере загрузки; файл кэшируется на диске и раздаётся,
пока приложение открыто.

**Architecture:** Новый изолированный «режим»: `TorrentEngineService` (singleton, обёртка над
MonoTorrent **streaming mode**) создаёт `TorrentSession` (observable); `MainViewModel` кладёт
`TorrentStreamViewModel` в `CurrentContent` → `TorrentStreamView` показывает прогресс, затем играет
поток через `LibVLCSharp.StreamMediaInput` + `MediaPlayer`. Данные пишутся на диск (кэш+раздача).

**Tech Stack:** WPF / .NET 8 / x64, LibVLCSharp 3.9.7.1 (уже есть), **MonoTorrent 3.0.2** (новый),
CommunityToolkit.Mvvm, DI (Microsoft.Extensions.Hosting).

**Спека:** `docs/superpowers/specs/2026-08-10-magnet-streaming-design.md` (читай перед работой).

## Global Constraints

- `PlatformTarget=x64`, `Prefer32Bit=false`, TFM `net8.0-windows` — не менять (нативные плагины
  LibVLC/структуры MonoTorrent).
- **Single-file publish ЗАПРЕЩЁН.** Публикация — только `dotnet publish src\Prosmotr\Prosmotr.csproj -c Release -o app`.
- После любых правок кода/XAML переопубликовывать в `app\` (ярлык пользователя запускает оттуда).
- Новая зависимость — **только** `MonoTorrent` 3.0.2. Никаких других пакетов без согласования.
- **Не коммитить в git без явного разрешения пользователя** (правило проекта). Все правки — в рабочем дереве.
- Стиль: комментарии на русском, «почему», а не «что»; nullable включён; MVVM/DI по существующим паттернам.
- UI-тексты — по-русски, тон приложения.
- Тесты — xUnit в `tests/Prosmotr.Tests` (net8.0-windows, x64), без сети/нативов.

## File Structure

| Файл | Ответственность |
|---|---|
| `tools/TorrentSpike/` (новый, временный, вне sln) | ручной спайк MonoTorrent+VLC, валидирует API до реализации |
| `src/Prosmotr/Infrastructure/MagnetLinkParser.cs` | валидация/парсинг магнет-ссылок (чистая логика) |
| `src/Prosmotr/Services/Torrent/TorrentFileSelector.cs` | выбор видеофайла в торренте (чистая логика) |
| `src/Prosmotr/Services/Torrent/TorrentStats.cs` | ETA, формат байт, «позиция за границей скачанного» (чистая логика) |
| `src/Prosmotr/Services/Torrent/TorrentCachePaths.cs` | пути кэша (чистая логика) |
| `src/Prosmotr/Services/Torrent/MagnetProtocolRegistration.cs` | регистрация magnet: протокола в HKCU |
| `src/Prosmotr/Models/TorrentStatus.cs` | enum статусов сессии |
| `src/Prosmotr/Models/TorrentSession.cs` | observable-модель сессии |
| `src/Prosmotr/Services/Torrent/ITorrentEngineService.cs` | интерфейс движка |
| `src/Prosmotr/Services/Torrent/TorrentEngineService.cs` | движок: ClientEngine, AddStreamingAsync, прогресс |
| `src/Prosmotr/ViewModels/TorrentStreamViewModel.cs` | VM экрана «магнет-стриминг» |
| `src/Prosmotr/Views/TorrentStreamView.xaml(.cs)` | экран: фаза загрузки + плеер |
| `src/Prosmotr/Views/MagnetInputWindow.xaml(.cs)` | диалог вставки ссылки |
| Modify: `src/Prosmotr/Prosmotr.csproj` | +PackageReference MonoTorrent |
| Modify: `src/Prosmotr/App.xaml.cs` | DI-регистрация, shutdown |
| Modify: `src/Prosmotr/Models/AppSettings.cs` | настройки магнета |
| Modify: `src/Prosmotr/ViewModels/SettingsViewModel.cs` + `Views/SettingsWindow.xaml` | секция настроек |
| Modify: `src/Prosmotr/Views/EmptyStateView.xaml` + `ViewModels/EmptyStateViewModel.cs` | кнопка входа |
| Modify: `src/Prosmotr/ViewModels/MainViewModel.cs`(+partial) | OpenMagnetAsync, CurrentContent, буфер/аргументы |
| Modify: `src/Prosmotr/Views/MainWindow.xaml(.cs)` | DataTemplate, диалог |
| Tests: `tests/Prosmotr.Tests/MagnetLinkParserTests.cs`, `TorrentFileSelectorTests.cs`, `TorrentStatsTests.cs`, `TorrentCachePathsTests.cs` | юнит-тесты чистой логики |
| Modify: `AGENTS.md` | раздел 5.36 с подводными камнями |

---

### Task 1: Спайк — MonoTorrent streaming + LibVLC (вручную, де-риск)

Проверяет САМЫЕ рискованные допущения до написания продакшен-кода: работает ли связка
`AddStreamingAsync` → `StreamProvider.CreateStreamAsync(prebuffer:true)` → `StreamMediaInput`
→ `MediaPlayer` на реальном магнете, и как ведёт себя seek.

**Files:**

- Create: `tools/TorrentSpike/TorrentSpike.csproj` (net8.0-windows, x64, OutDir не трогаем)
- Create: `tools/TorrentSpike/Program.cs`

**Interfaces:**

- Consumes: пакеты `MonoTorrent` 3.0.2, `LibVLCSharp` 3.9.7.1, `VideoLAN.LibVLC.Windows` 3.0.23.1 (скопируй версии из `Prosmotr.csproj`).
- Produces: **зафиксированные имена API** для Task 5 (см. Acceptance). Спайк — временный; после
  проверки удалить папку `tools/TorrentSpike` (не вносить в sln).

- [ ] **Step 1: Создать проект спайка**

```bash
dotnet new console -n TorrentSpike -o tools/TorrentSpike -f net8.0
```

Добавь в csproj:

```xml
<PropertyGroup>
  <TargetFramework>net8.0-windows</TargetFramework>
  <PlatformTarget>x64</PlatformTarget>
  <Prefer32Bit>false</Prefer32Bit>
  <ImplicitUsings>enable</ImplicitUsings>
  <Nullable>enable</Nullable>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="MonoTorrent" Version="3.0.2" />
  <PackageReference Include="LibVLCSharp" Version="3.9.7.1" />
  <PackageReference Include="VideoLAN.LibVLC.Windows" Version="3.0.23.1" />
</ItemGroup>
```

- [ ] **Step 2: Написать Program.cs (полный листинг)**

```csharp
using LibVLCSharp.Shared;
using MonoTorrent;
using MonoTorrent.Client;

if (args.Length == 0 || !MagnetLink.TryParse(args[0], out var magnet))
{
    Console.WriteLine("Usage: TorrentSpike <magnet-link> [saveDir]");
    return;
}

Core.Initialize();
using var libVlc = new LibVLC("--quiet");

var saveDir = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "torrentspike");
Directory.CreateDirectory(saveDir);

// 1) Движок: запасной порт, DHT, без форвардинга (спайк — локально)
var engine = new ClientEngine(new EngineSettings
{
    CacheDirectory = Path.Combine(saveDir, ".cache"),
    ListenEndPoints = new Dictionary<string, System.Net.IPEndPoint>
    {
        ["tcp"] = new(System.Net.IPAddress.Any, 0)
    },
    AllowPortForwarding = false,
    AutoSaveLoadFastResume = true
});

Console.WriteLine($"Adding magnet: {magnet.InfoHashes}");
var manager = await engine.AddStreamingAsync(magnet, saveDir);

// 2) Метаданные с таймаутом 60 с
var metadataTask = manager.WaitForMetadataAsync();
if (await Task.WhenAny(metadataTask, Task.Delay(60_000)) != metadataTask)
{
    Console.WriteLine("FAIL: metadata timeout (no peers?)");
    await engine.RemoveAsync(manager, RemoveMode.KeepAllData);
    return;
}
await metadataTask;
Console.WriteLine($"Name: {manager.Torrent!.Name}");
foreach (var f in manager.Files)
    Console.WriteLine($"  file: {f.Path}  len={f.Length}  pieces={f.StartPieceIndex}..{f.EndPieceIndex}");

// 3) Выбираем самый большой видеофайл (проверяем ToHex у InfoHash)
Console.WriteLine($"InfoHash hex: {manager.InfoHashes.V1.ToHex()} / V2 present: {manager.InfoHashes.V2 is not null}");

var file = manager.Files.OrderByDescending(f => f.Length).First();
Console.WriteLine($"Selected: {file.Path} ({file.Length} bytes)");

// 4) Поток с prebuffer (первые+последние куски)
var stream = await manager.StreamProvider!.CreateStreamAsync(file, prebuffer: true);
Console.WriteLine($"Stream ready, CanSeek={stream.CanSeek}, len={stream.Length}");

// 5) Играем поток через кастомный IO
using var media = new Media(libVlc, new StreamMediaInput(stream));
using var player = new MediaPlayer(libVlc);
player.Play(media);
await Task.Delay(10_000);
Console.WriteLine($"IsPlaying={player.IsPlaying}, Position={player.Time}ms of {player.Length}ms");
Console.WriteLine($"Downloaded={manager.Bitfield.PercentComplete:F1}%, down={manager.Monitor.DownloadSpeed}B/s, peers={manager.Peers.ConnectedPeers.Count}");

// 6) Seek вперёд на 25% — проверить блокирующее поведение
player.Time = (long)(player.Length * 0.25);
await Task.Delay(5_000);
Console.WriteLine($"After seek: Position={player.Time}ms, Downloaded={manager.Bitfield.PercentComplete:F1}%");

player.Stop();
await engine.RemoveAsync(manager, RemoveMode.KeepAllData);
Console.WriteLine("DONE");
```

- [ ] **Step 3: Запустить с реальным магнетом**

Нужен магнет легального контента (например, свободный фильм Sintel или любой твой).
Запуск:

```bash
dotnet run --project tools/TorrentSpike -- "<magnet>"
```

Expected: печатается имя торрента, файлы, `Stream ready`, `IsPlaying=True` за ~10–30 с
(при живой раздаче), позиция/прогресс растут. Seek вперёд либо перематывает (если докачано),
либо поток блокируется — `IsPlaying` остаётся true, Position не прыгает назад.

- [ ] **Step 4: Зафиксировать результат (Acceptance)**

Запиши в конец этого файла (или в комментарий в коде спайка) **фактические** имена API, которые
будут использоваться в Task 5. Что обязательно подтвердить/зафиксировать:

- `EngineSettings` — инициализатор без builder работает? порт `0` допустим?
- `InfoHashes.V1.ToHex()` — имя метода (или замена).
- `AddStreamingAsync` автозапускает загрузку или нужен `manager.StartAsync()`?
- `StreamMediaInput` + VLC: играет? seek за границу не роняет плеер?
- Скорость скачивания на твоём канале (ориентир для UI).
- Если что-то из этого отличается от ожиданий — пометь и скорректируй Task 5 при выполнении.

- [ ] **Step 5: Удалить спайк** (после записи результатов)

```bash
rm -rf tools/TorrentSpike
```

Коммит не делать (правило проекта).

---

### Task 2: MagnetLinkParser (чистая логика, TDD)

Валидация для UI (диалог, буфер обмена, аргументы). Ленящая — пропускает лишние параметры;
авторитетная проверка остаётся у `MagnetLink.TryParse` в движке.

**Files:**

- Create: `src/Prosmotr/Infrastructure/MagnetLinkParser.cs`
- Test: `tests/Prosmotr.Tests/MagnetLinkParserTests.cs`

**Interfaces:**

- Produces:
  - `public static bool IsValidMagnet(string? input)` — непустая строка, префикс `magnet:`,
    параметр `xt=urn:btih:` (case-insensitive) с непустым хешем.
  - `public static bool TryGetInfoHash(string? input, out string infoHash)` — извлекает значение
    `xt=urn:btih:<hash>`, хеш приводится к нижнему регистру; false если нет.

- [ ] **Step 1: Написать падающий тест**

```csharp
using Prosmotr.Infrastructure;
using Xunit;

namespace Prosmotr.Tests;

public sealed class MagnetLinkParserTests
{
    [Fact]
    public void IsValidMagnet_PlainText_False() =>
        Assert.False(MagnetLinkParser.IsValidMagnet("hello world"));

    [Fact]
    public void IsValidMagnet_NonBtihXt_False() =>
        Assert.False(MagnetLinkParser.IsValidMagnet("magnet:?xt=urn:sha1:abc"));

    [Fact]
    public void IsValidMagnet_EmptyHash_False() =>
        Assert.False(MagnetLinkParser.IsValidMagnet("magnet:?xt=urn:btih:"));

    [Fact]
    public void IsValidMagnet_Valid_FullLink() =>
        Assert.True(MagnetLinkParser.IsValidMagnet(
            "magnet:?xt=urn:btih:08ada5a7a6183aae1e09d831df6748d566095a10&dn=Sintel&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337"));

    [Fact]
    public void IsValidMagnet_UppercaseSchemeAndXt_True()
    {
        Assert.True(MagnetLinkParser.IsValidMagnet(
            "MAGNET:?XT=URN:BTIH:08ada5a7a6183aae1e09d831df6748d566095a10"));
    }

    [Fact]
    public void TryGetInfoHash_ReturnsLowercaseHash()
    {
        Assert.True(MagnetLinkParser.TryGetInfoHash(
            "magnet:?xt=urn:btih:08ADA5A7A6183AAE1E09D831DF6748D566095A10&dn=X", out var hash));
        Assert.Equal("08ada5a7a6183aae1e09d831df6748d566095a10", hash);
    }

    [Fact]
    public void TryGetInfoHash_Invalid_False()
    {
        Assert.False(MagnetLinkParser.TryGetInfoHash("not a magnet", out _));
        Assert.False(MagnetLinkParser.TryGetInfoHash(null, out _));
    }
}
```

- [ ] **Step 2: Прогнать тест — убедиться, что падает**

```bash
dotnet test tests/Prosmotr.Tests/Prosmotr.Tests.csproj --filter MagnetLinkParserTests
```

Expected: FAIL (тип не существует).

- [ ] **Step 3: Реализовать**

```csharp
using System.Text.RegularExpressions;

namespace Prosmotr.Infrastructure;

/// <summary>Ленящая валидация магнет-ссылок для UI (диалог, буфер обмена, аргументы).
/// Авторитетная проверка — MonoTorrent.MagnetLink.TryParse в движке; здесь только отсекаем
/// очевидный мусор, чтобы не дёргать движок.</summary>
public static partial class MagnetLinkParser
{
    [GeneratedRegex(@"^magnet:\?xt=urn:btih:([0-9a-f]{32,40})$", RegexOptions.IgnoreCase)]
    private static partial Regex MagnetXtRegex();

    public static bool IsValidMagnet(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;
        var match = MagnetXtRegex().Match(input.Trim());
        return match.Success;
    }

    public static bool TryGetInfoHash(string? input, out string infoHash)
    {
        infoHash = string.Empty;
        if (string.IsNullOrWhiteSpace(input)) return false;
        var match = MagnetXtRegex().Match(input.Trim());
        if (!match.Success) return false;
        infoHash = match.Groups[1].Value.ToLowerInvariant();
        return true;
    }
}
```

- [ ] **Step 4: Прогнать тест — PASS**
- [ ] **Step 5: Проверить LSP-диагностику** (`lsp_diagnostics` на файл) — чисто.

---

### Task 3: TorrentFileSelector (чистая логика, TDD)

Выбор видеофайла в торренте: самый большой файл с видео-расширением.

**Files:**

- Create: `src/Prosmotr/Services/Torrent/TorrentFileSelector.cs` (вместе с `TorrentFileEntry` record)
- Test: `tests/Prosmotr.Tests/TorrentFileSelectorTests.cs`

**Interfaces:**

- Produces:
  - `public sealed record TorrentFileEntry(string Path, long Length);`
  - `public static TorrentFileEntry? SelectVideoFile(IEnumerable<TorrentFileEntry> files)`
    — null, если видео нет. Видео-расширения — `SupportedFormats.VideoExtensions` (OrdinalIgnoreCase).

- [ ] **Step 1: Падающий тест**

```csharp
using Prosmotr.Services.Torrent;
using Xunit;

namespace Prosmotr.Tests;

public sealed class TorrentFileSelectorTests
{
    [Fact]
    public void SelectVideoFile_Empty_ReturnsNull() =>
        Assert.Null(TorrentFileSelector.SelectVideoFile(Array.Empty<TorrentFileEntry>()));

    [Fact]
    public void SelectVideoFile_NoVideo_ReturnsNull()
    {
        var files = new[] { new TorrentFileEntry("readme.txt", 100), new TorrentFileEntry("cover.jpg", 200) };
        Assert.Null(TorrentFileSelector.SelectVideoFile(files));
    }

    [Fact]
    public void SelectVideoFile_PicksLargestVideo_IgnoringBiggerNonVideo()
    {
        var files = new[]
        {
            new TorrentFileEntry("Movie/movie.mkv", 1_000_000),
            new TorrentFileEntry("Movie/sample.mp4", 50_000_000), // sample меньше основного — не выбираем
            new TorrentFileEntry("Movie/extra.bin", 900_000_000)  // не видео — игнор
        };
        var selected = TorrentFileSelector.SelectVideoFile(files);
        Assert.NotNull(selected);
        Assert.Equal("Movie/movie.mkv", selected!.Path);
    }

    [Fact]
    public void SelectVideoFile_UppercaseExtension_Selected()
    {
        var files = new[] { new TorrentFileEntry("clip.MKV", 42), new TorrentFileEntry("a.mp4", 10) };
        Assert.Equal("clip.MKV", TorrentFileSelector.SelectVideoFile(files)!.Path);
    }

    [Fact]
    public void SelectVideoFile_VideoInSubfolder_Wins()
    {
        var files = new[] { new TorrentFileEntry("season/ep1.mp4", 500), new TorrentFileEntry("season/ep2.avi", 300) };
        Assert.Equal("season/ep1.mp4", TorrentFileSelector.SelectVideoFile(files)!.Path);
    }
}
```

- [ ] **Step 2: Прогнать — FAIL**
- [ ] **Step 3: Реализовать**

```csharp
using Prosmotr.Infrastructure;

namespace Prosmotr.Services.Torrent;

/// <summary>Описание файла внутри торрента (чистая проекция MonoTorrent.ITorrentManagerFile —
/// чтобы селектор тестировался без сети).</summary>
public sealed record TorrentFileEntry(string Path, long Length);

/// <summary>Выбор видеофайла для воспроизведения: самый большой файл с видео-расширением
/// (в v1 торренты — одиночные фильмы; сезонные папки — вне скоупа, берём максимум).</summary>
public static class TorrentFileSelector
{
    public static TorrentFileEntry? SelectVideoFile(IEnumerable<TorrentFileEntry> files)
    {
        TorrentFileEntry? best = null;
        foreach (var file in files)
        {
            var ext = System.IO.Path.GetExtension(file.Path);
            if (!SupportedFormats.VideoExtensions.Contains(ext)) continue;
            if (best == null || file.Length > best.Length) best = file;
        }
        return best;
    }
}
```

- [ ] **Step 4: Прогнать — PASS**

---

### Task 4: TorrentStats + TorrentCachePaths (чистая логика, TDD)

**Files:**

- Create: `src/Prosmotr/Services/Torrent/TorrentStats.cs`
- Create: `src/Prosmotr/Services/Torrent/TorrentCachePaths.cs`
- Test: `tests/Prosmotr.Tests/TorrentStatsTests.cs`, `tests/Prosmotr.Tests/TorrentCachePathsTests.cs`

**Interfaces:**

- `TorrentStats`:
  - `public static long? ComputeEtaSeconds(long remainingBytes, long bytesPerSecond)`
    — null при speed <= 0; 0 при remaining <= 0.
  - `public static string FormatBytes(long bytes)` — «12.3 МБ», «456 КБ», «1.2 ГБ».
  - `public static bool IsBeyondDownloaded(long positionMs, long lengthMs, double downloadedPercent, long slackMs)`
    — position > (length * percent/100) + slack → «докачивается». length <= 0 → false.
- `TorrentCachePaths`:
  - `public static string DefaultCacheDirectory` — `%LOCALAPPDATA%\Prosmotr\torrents`.
  - `public static string SessionDirectory(string cacheRoot, string infoHashHex)` — `cacheRoot\<hash>` (lower).
  - `public static string SaveDirectoryFor(string cacheRoot, string infoHashHex)` — SessionDirectory + `\data`.

- [ ] **Step 1: Падающие тесты** (оба файла)

```csharp
using Prosmotr.Services.Torrent;
using Xunit;

namespace Prosmotr.Tests;

public sealed class TorrentStatsTests
{
    [Theory]
    [InlineData(0, 0, null)]
    [InlineData(100, 0, null)]
    [InlineData(0, 1000, 0L)]
    [InlineData(1_000_000, 500_000, 2L)]
    [InlineData(1_000_000, 100_000, 10L)]
    public void ComputeEtaSeconds_Works(long remaining, long speed, long? expected) =>
        Assert.Equal(expected, TorrentStats.ComputeEtaSeconds(remaining, speed));

    [Theory]
    [InlineData(0, "0 Б")]
    [InlineData(500, "500 Б")]
    [InlineData(2 * 1024, "2.0 КБ")]
    [InlineData(5 * 1024 * 1024, "5.0 МБ")]
    [InlineData((long)(1.25 * 1024 * 1024 * 1024), "1.3 ГБ")]
    public void FormatBytes_Works(long bytes, string expected) =>
        Assert.Equal(expected, TorrentStats.FormatBytes(bytes));

    [Fact]
    public void IsBeyondDownloaded_True_WhenPastDownloadedPlusSlack() =>
        Assert.True(TorrentStats.IsBeyondDownloaded(positionMs: 6_000, lengthMs: 10_000, downloadedPercent: 50, slackMs: 500));

    [Fact]
    public void IsBeyondDownloaded_False_WhenWithinDownloaded() =>
        Assert.False(TorrentStats.IsBeyondDownloaded(positionMs: 4_000, lengthMs: 10_000, downloadedPercent: 50, slackMs: 500));

    [Fact]
    public void IsBeyondDownloaded_False_WhenLengthUnknown() =>
        Assert.False(TorrentStats.IsBeyondDownloaded(5_000, 0, 50, 500));
}

public sealed class TorrentCachePathsTests
{
    [Fact]
    public void DefaultCacheDirectory_IsUnderLocalAppData()
    {
        var dir = TorrentCachePaths.DefaultCacheDirectory;
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), dir);
        Assert.EndsWith("torrents", dir);
    }

    [Fact]
    public void SessionDirectory_UsesLowercaseHash()
    {
        var dir = TorrentCachePaths.SessionDirectory(@"C:\cache", "ABC123");
        Assert.Equal(@"C:\cache\abc123", dir);
    }

    [Fact]
    public void SaveDirectoryFor_IsUnderSession()
    {
        var dir = TorrentCachePaths.SaveDirectoryFor(@"C:\cache", "abc");
        Assert.Equal(@"C:\cache\abc\data", dir);
    }
}
```

- [ ] **Step 2: Прогнать — FAIL**
- [ ] **Step 3: Реализовать**

```csharp
using System.Globalization;

namespace Prosmotr.Services.Torrent;

/// <summary>Чистые вычисления для UI загрузки (ETA, формат байт, «позиция за границей
/// скачанного») — без зависимости от MonoTorrent, покрыты юнит-тестами.</summary>
public static class TorrentStats
{
    public static long? ComputeEtaSeconds(long remainingBytes, long bytesPerSecond)
    {
        if (bytesPerSecond <= 0) return null;
        if (remainingBytes <= 0) return 0;
        return (remainingBytes + bytesPerSecond - 1) / bytesPerSecond;
    }

    public static string FormatBytes(long bytes)
    {
        const long kb = 1024, mb = 1024 * 1024, gb = 1024L * 1024 * 1024;
        return bytes switch
        {
            >= gb => $"{bytes / (double)gb:0.0} ГБ",
            >= mb => $"{bytes / (double)mb:0.0} МБ",
            >= kb => $"{bytes / (double)kb:0.0} КБ",
            _ => $"{bytes} Б"
        };
    }

    /// <summary>Позиция воспроизведения дальше, чем скачано (с запасом) → плеер ждёт докачки.</summary>
    public static bool IsBeyondDownloaded(long positionMs, long lengthMs, double downloadedPercent, long slackMs)
    {
        if (lengthMs <= 0) return false;
        var downloadedMs = lengthMs * (downloadedPercent / 100.0);
        return positionMs > downloadedMs + slackMs;
    }
}
```

```csharp
namespace Prosmotr.Services.Torrent;

/// <summary>Пути кэша магнет-стриминга. По умолчанию — %LOCALAPPDATA%\Prosmotr\torrents.
/// Сессия = папка по infoHash (нижний регистр); данные торрента — в подпапке data.</summary>
public static class TorrentCachePaths
{
    public static string DefaultCacheDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Prosmotr", "torrents");

    public static string SessionDirectory(string cacheRoot, string infoHashHex) =>
        Path.Combine(cacheRoot, infoHashHex.ToLowerInvariant());

    public static string SaveDirectoryFor(string cacheRoot, string infoHashHex) =>
        Path.Combine(SessionDirectory(cacheRoot, infoHashHex), "data");
}
```

- [ ] **Step 4: Прогнать — PASS**

---

### Task 5: TorrentSession + TorrentStatus (модели)

**Files:**

- Create: `src/Prosmotr/Models/TorrentStatus.cs`
- Create: `src/Prosmotr/Models/TorrentSession.cs`

**Interfaces:**

- `public enum TorrentStatus { ResolvingMetadata, Downloading, ReadyToPlay, Playing, Stopped, Error }`
- `TorrentSession : ObservableObject` (CommunityToolkit):
  - `[ObservableProperty] TorrentStatus status;` (init `ResolvingMetadata`)
  - `[ObservableProperty] string? name;`
  - `[ObservableProperty] double downloadedPercent;`
  - `[ObservableProperty] long downloadSpeed;`
  - `[ObservableProperty] long uploadSpeed;`
  - `[ObservableProperty] int peersCount;`
  - `[ObservableProperty] long? etaSeconds;`
  - `[ObservableProperty] string? selectedFilePath;`
  - `[ObservableProperty] string? errorMessage;`
  - `[ObservableProperty] bool isReadyToPlay;` — true после создания потока
  - `public Stream? Stream { get; set; }` — НЕ observable (поток отдаётся плееру напрямую)
  - `public long TotalBytes { get; set; }`
  - `public string? InfoHashHex { get; set; }` — для папки/логов
  - `public string? SaveDirectory { get; set; }`
  - `public object? EngineRef { get; set; }` — хук: движок кладёт сюда `TorrentManager`, чтобы
    сессия не зависела от MonoTorrent (VM/View не видят движок).

**Почему так:** VM/View/тесты не касаются MonoTorrent-типов; движок — единственный, кто видит
`TorrentManager`. `EngineRef` — «непрозрачный» дескриптор для CloseSession.

- [ ] **Step 1: Реализовать оба файла** (модели без поведения; тестов не требуется —
  CommunityToolkit генерирует INPC; проверяется компиляцией).
- [ ] **Step 2: Собрать**

```bash
dotnet build src/Prosmotr/Prosmotr.csproj -c Debug
```

Expected: 0 errors.

---

### Task 6: TorrentEngineService (ядро)

**Files:**

- Create: `src/Prosmotr/Services/Torrent/ITorrentEngineService.cs`
- Create: `src/Prosmotr/Services/Torrent/TorrentEngineService.cs`
- Modify: `src/Prosmotr/App.xaml.cs` (регистрация в DI)

**Interfaces:**

- `public interface ITorrentEngineService`
  - `Task<TorrentSession> AddMagnetAsync(string magnet, CancellationToken ct)`
    — быстро возвращает сессию в статусе `ResolvingMetadata`; инициализация идёт в фоне,
    сессия сама доходит до `ReadyToPlay`/`Error`.
  - `TorrentSession? GetActiveSession()`
  - `Task CloseSessionAsync()` — стоп активной сессии (keep data; удаляет данные, если
    `DeleteTorrentCacheOnExit`).
  - `Task ShutdownAsync()` — для `App.OnExit`.
- Класс: ctor(`ISettingsService`); лениво создаёт `ClientEngine` (один на процесс).
  - `EngineSettings` по результатам спайка: `CacheDirectory = <cache>\\.cache`,
    `ListenEndPoints = {["tcp"] = new IPEndPoint(IPAddress.Any, 0)}`, `AllowPortForwarding = false`,
    `AutoSaveLoadFastResume = true`.
  - `AddMagnetAsync`: `MagnetLink.TryParse` (false → throw `FormatException`), создаёт сессию,
    вызывает `AddStreamingAsync(magnet, saveDir)`, кладёт `manager` в `EngineRef`, запускает
    фоновый `InitializeAsync` (не await), возвращает сессию.
  - `InitializeAsync`: `WaitForMetadataAsync` с таймаутом 60 с (через `Task.WhenAny`); на успех —
    `session.Name = manager.Torrent!.Name`, `session.TotalBytes = selected.Length`,
    `session.SelectedFilePath = Path.Combine(saveDir, file.Path)`, статус `Downloading`; затем
    `CreateStreamAsync(file, prebuffer: true)`; на успех — `session.Stream = stream`,
    `session.IsReadyToPlay = true`, статус `ReadyToPlay`. Ошибка/таймаут → `session.Status = Error`,
    `session.ErrorMessage = …` (русский текст), лог в `AppLog`.
  - Прогресс: `DispatcherTimer` (1 с) — читает `manager.Bitfield.PercentComplete`,
    `manager.Monitor.DownloadSpeed/UploadSpeed`, `manager.Peers.ConnectedPeers.Count`,
    `manager.Torrent!.Size` → `etaSeconds` через `TorrentStats.ComputeEtaSeconds`.
  - `CloseSessionAsync`: `engine.RemoveAsync(manager, mode)` (`KeepAllData` или
    `CacheDataAndDownloadedData` при `DeleteTorrentCacheOnExit`), dispose потока, остановить таймер.
  - `ShutdownAsync`: `CloseSessionAsync` для активной сессии.
- DI: `services.AddSingleton<ITorrentEngineService, TorrentEngineService>();` — рядом с другими
  singletons в `App.ConfigureServices`.

- [ ] **Step 1: Написать интерфейс и класс** по листингу выше; подставь **имена API из спайка**
  (Task 1 Step 4), если они отличаются.
- [ ] **Step 2: Зарегистрировать в DI** (`App.xaml.cs`, блок singletons).
- [ ] **Step 3: Собрать** — `dotnet build src\Prosmotr\Prosmotr.csproj -c Debug`, 0 errors.
- [ ] **Step 4: Smoke-проверка** — временно вызови `AddMagnetAsync` из `MainViewModel.InitializeAsync`
  (см. Task 9) и проверь по `app.log`, что сессия доходит до `ReadyToPlay`; после — убрать временный вызов.

---

### Task 7: TorrentStreamViewModel

**Files:**

- Create: `src/Prosmotr/ViewModels/TorrentStreamViewModel.cs`

**Interfaces:**

- ctor: `TorrentSession session, LibVlcProvider vlc, ISettingsService settings, IDialogService dialog, INotificationService notify, Func<Task> closeRequested`
- `[ObservableProperty] bool isBuffering;` — cover/оверлей «докачивается…»
- `[ObservableProperty] bool isPlaying;`
- `[ObservableProperty] long positionMs;`
- `[ObservableProperty] long lengthMs;`
- `[ObservableProperty] int volume;` `[ObservableProperty] bool isMuted;`
- Свойства для просмотра из сессии: `Name`, `DownloadedPercent`, `DownloadSpeed`, `UploadSpeed`,
  `PeersCount`, `EtaText`, `StatusText` (читаются из `session`, подписываемся на
  `session.PropertyChanged`).
- `public MediaPlayer? Player { get; private set; }` — вью подключает его к `VideoView`.
- Методы:
  - `Task PlayAsync()` — если `session.IsReadyToPlay && Player == null`:
    `Player = new MediaPlayer(vlc.LibVlc) { EnableHardwareDecoding = false };`
    `Player.Play(new Media(vlc.LibVlc, new StreamMediaInput(session.Stream!)));`
    подписка `Playing` (session.Status = Playing, isPlaying=true), `Paused`, `Stopped`,
    `EndReached`, `TimeChanged` → `PositionMs` (+буферинг-проверка), `LengthChanged` → `LengthMs`.
    Внимание: `Media` держать в поле (не GC), освобождать в Stop.
  - `TogglePlayPause()`, `ToggleMute()`, `SetVolume(int)`.
  - `StopAndRelease()` — паттерн `VideoViewerViewModel.StopAndRelease`: `Player.Stop()`,
    `Media.Dispose()`, `Player.Dispose()`, отписки. Идемпотентно, защищено флагом.
  - `ToggleFullScreen()` — поднять событие `FullScreenRequested` (обрабатывает MainWindow через
    `FullScreenHelper` + `DeferFullScreenTransition` — см. подводный камень 5.12/5.16: переход
    НЕ синхронно в Click, а `Mouse.Capture(null)` + `Dispatcher.BeginInvoke(…, Input)`).
  - `CloseSession()` → `await closeRequested()` (MainViewModel закроет сессию и вернёт EmptyState).
  - Буферинг: в обработчике `TimeChanged` — `IsBuffering = TorrentStats.IsBeyondDownloaded(
    PositionMs, LengthMs, session.DownloadedPercent, slackMs: 3000)`.
- `IDisposable` — `StopAndRelease` (для `UpdateCurrentContent`).

- [ ] **Step 1: Реализовать VM** по листингу (уточнения из Task 6 по StreamMediaInput).
- [ ] **Step 2: Собрать** — 0 errors.
- [ ] **Step 3: (чистая логика уже покрыта Task 4; здесь ручная проверка позже, Task 12).**

---

### Task 8: TorrentStreamView (экран)

**Files:**

- Create: `src/Prosmotr/Views/TorrentStreamView.xaml`
- Create: `src/Prosmotr/Views/TorrentStreamView.xaml.cs`

**Интерфейс XAML** (структура; стили — по образцу `VideoViewerView`):

```
UserControl (Background=AppCanvasBrush)
└─ Grid
   ├─ Фаза загрузки (Visibility = !IsReadyToPlay):
   │   StackPanel по центру:
   │     SymbolIcon "MovieCamera24", Name (TextBlock)
   │     ProgressBar (DownloadedPercent), "42% · 12.3 МБ/с ↓ · 5.2 МБ/с ↑ · 14 пиров"
   │     EtaText «Осталось ~2 мин», кнопка «Отмена» → CloseSessionCommand
   ├─ Фаза воспроизведения (Visibility = IsReadyToPlay):
   │   vlc:VideoView x:Name="Video" (xmlns:vlc="clr-namespace:LibVLCSharp.WPF;assembly=LibVLCSharp.WPF")
   │   └─ Overlay (Grid #02000000, как в VideoViewerView):
   │       ├─ SwitchCover (Border чёрный, Visibility=IsBuffering) — скрывает белый фон нативного HWND
   │       ├─ BufferingPanel «Докачивается…» (Visibility=IsBuffering)
   │       └─ ControlBar (Visibility=IsReadyToPlay && !IsBuffering):
   │            Play/Pause, Seek Slider (Maximum=LengthMs, Value=PositionMs),
   │            Volume, Mute, FullScreen, Close — кнопки ui:Button + AutomationProperties.Name
```

**Code-behind:**

- `OnDataContextChanged`/`OnLoaded`: `AttachVm(vm)` — идемпотентно (`-=`, затем `+=` PropertyChanged),
  как в `VideoViewerView.AttachMainVm` (gotcha 5.23); `Video.MediaPlayer = vm.Player`; при
  `vm.IsReadyToPlay` → `await vm.PlayAsync()` (через `Dispatcher.BeginInvoke(Render)` после cover).
- `UpdateCover()`/`UpdateBuffering()` по `PropertyChanged` (`IsBuffering`, `IsPlaying`, `IsReadyToPlay`).
- Клавиши: `PreviewKeyDown` на UserControl — Space (play/pause), Esc (закрыть/полный экран —
  вью шлёт `CloseSession`/`FullScreen` в VM), `→` (seek +10s), `←` (−10s) в пределах LengthMs.
- `OnUnloaded`: `vm?.StopAndRelease()` (как в `VideoViewerView.OnUnloaded`) — НО не при PiP (PiP тут нет).

- [ ] **Step 1: Написать XAML** (структура выше, стили по образцу `VideoViewerView.xaml`).
- [ ] **Step 2: Написать code-behind** (AttachVm, cover, PlayAsync, ключи).
- [ ] **Step 3: Собрать** — 0 errors.
- [ ] **Step 4: Ручная проверка позже (Task 12).**

---

### Task 9: Входы — кнопка, диалог, буфер, magnet: аргумент

**Files:**

- Create: `src/Prosmotr/Views/MagnetInputWindow.xaml(.cs)`
- Modify: `src/Prosmotr/Views/EmptyStateView.xaml`
- Modify: `src/Prosmotr/ViewModels/EmptyStateViewModel.cs`
- Modify: `src/Prosmotr/ViewModels/MainViewModel.cs` (или новый `MainViewModel.Torrent.cs`)
- Modify: `src/Prosmotr/Views/MainWindow.xaml(.cs)`
- Modify: `src/Prosmotr/App.xaml.cs` (парсинг аргумента `magnet:`)
- Create: `src/Prosmotr/Services/Torrent/MagnetProtocolRegistration.cs`

**Интерфейсы:**

- `MainViewModel`:
  - Поле `private readonly CancellationTokenSource _torrentCts = new();` — отдельный токен для
    торрент-сессии (не путать с `_openCts` галереи); освобождается в `Dispose`.
  - `[RelayCommand] void OpenMagnet() => MagnetInputRequested?.Invoke();` (событие — как
    `PropertiesRequested` в gotcha 5.10).
  - `public event Action? MagnetInputRequested;`
  - `public async Task OpenMagnetAsync(string magnet)` — валидация `MagnetLinkParser.IsValidMagnet`
    (иначе тост «Неверная магнет-ссылка»), закрыть PiP, `var session = await _torrents.AddMagnetAsync(
    magnet, _torrentCts.Token)`; `CurrentContent = new TorrentStreamViewModel(...)` (через DI-фабрику
    `Func<TorrentSession, TorrentStreamViewModel>`); если `AddMagnetAsync` бросил `FormatException`
    — тост, иначе — тот же тост «Не удалось открыть».
  - В `UpdateCurrentContent`: при уходе со `TorrentStreamViewModel` — `vm.StopAndRelease()` +
    `await _torrents.CloseSessionAsync()` (старый контент освобождается через `IDisposable`
    с задержкой, как в gotcha 5.4 — паттерн `_pendingDisposal`).
  - `InitializeAsync`: если аргументы содержат `magnet:` (или в args есть строка,
    `IsValidMagnet`) → `OpenMagnetAsync(arg)` вместо открытия файла. Если старт пустой и
    буфер обмена содержит валидный магнет (`Clipboard.GetText()`) → открыть диалог с
    предзаполненной ссылкой (после короткой задержки, чтобы окно успело показаться).
- `EmptyStateViewModel`: добавить ctor-параметр `Func<Task> openMagnet` и
  `[RelayCommand] private Task OpenMagnet() => _openMagnet();`; в `MainViewModel.CreateEmptyState`
  передать `() => OpenMagnetAsync`-команду (через `OpenMagnetCommand.Execute(null)`).
- `MagnetInputWindow`: `FluentWindow`, TextBox (предзаполняется), OK/Отмена; результат —
  `public string? Magnet` (null если отменено); `Validate` по `MagnetLinkParser.IsValidMagnet`.
  `MainWindow`: обработчик `MagnetInputRequested` → `new MagnetInputWindow { Owner = this }` +
  `ShowDialog()`, при валидном результате — `await vm.OpenMagnetAsync(result)`.
- `MagnetProtocolRegistration` (static):
  - `Register()`: HKCU `Software\Classes\magnet\shell\open\command` → `"<exe>" "%1"`
    (паттерн `FileAssociationService`; exe = `Environment.ProcessPath`).
  - `Unregister()`: удалить `Software\Classes\magnet`.
  - `IsRegistered`: ключ существует.
  - Вызовы: при старте (`App.TryIntegrateShell`-подобно: если `RegisterMagnetProtocol` → Register,
    иначе Unregister) и в `SettingsViewModel` при переключении тумблера (try/catch + resync).

- [ ] **Step 1: MagnetInputWindow** (XAML+code-behind, валидация).
- [ ] **Step 2: EmptyStateView + EmptyStateViewModel** — кнопка «Смотреть по магнет-ссылке…»
  (SymbolIcon `Link24`? — проверь доступные в WPF-UI; подойдёт `MovieCamera24` или `Globe24`)
  под существующими кнопками «Открыть файл/папку».
- [ ] **Step 3: MainViewModel** — событие, `OpenMagnetAsync`, фабрика VM в DI
  (`services.AddTransient<Func<TorrentSession, TorrentStreamViewModel>>(sp => session =>
     new TorrentStreamViewModel(session, sp.GetRequiredService<LibVlcProvider>(), …))`).
- [ ] **Step 4: MainWindow** — DataTemplate `TorrentStreamViewModel → TorrentStreamView`
  (в `Window.Resources`), обработчик `MagnetInputRequested`.
- [ ] **Step 5: InitializeAsync** — magnet-аргумент и буфер обмена (см. выше).
- [ ] **Step 6: MagnetProtocolRegistration** + вызовы (старт + тумблер).
- [ ] **Step 7: Собрать** — 0 errors.

---

### Task 10: Настройки (AppSettings + окно)

**Files:**

- Modify: `src/Prosmotr/Models/AppSettings.cs`
- Modify: `src/Prosmotr/ViewModels/SettingsViewModel.cs`
- Modify: `src/Prosmotr/Views/SettingsWindow.xaml`

**Изменения:**

- `AppSettings` (секция `// --- Магнет-стриминг ---`):
  - `public string? TorrentCacheDirectory { get; set; }` — null → дефолт
    (`TorrentCachePaths.DefaultCacheDirectory`).
  - `public bool DeleteTorrentCacheOnExit { get; set; } = false;`
  - `public bool RegisterMagnetProtocol { get; set; } = false;`
- `SettingsViewModel`: `[ObservableProperty] bool deleteTorrentCacheOnExit;`
  `[ObservableProperty] bool registerMagnetProtocol;` — чтение в ctor, `Commit`:
  `deleteTorrentCacheOnExit` — `SaveDebounced()`; `registerMagnetProtocol` —
  `Commit(immediate: true)` + вызов `MagnetProtocolRegistration` (try/catch + resync
  `registerMagnetProtocol = MagnetProtocolRegistration.IsRegistered` при ошибке) — паттерн
  `OnIntegrateShellChanged` (gotcha 5.24).
- `SettingsWindow.xaml`: новая секция «Магнет-стриминг» (ToggleSwitch «Удалять скачанное при
  выходе», «Открывать magnet-ссылки этим приложением»; текст-подсказка про папку кэша).

- [ ] **Step 1: AppSettings + SettingsViewModel + окно** (по образцу существующих тумблеров).
- [ ] **Step 2: Собрать** — 0 errors.
- [ ] **Step 3: Прогнать юнит-тесты** — старые тесты настроек не должны сломаться
  (`dotnet test tests/Prosmotr.Tests/Prosmotr.Tests.csproj`).

---

### Task 11: Shutdown + AGENTS.md + публикация

**Files:**

- Modify: `src/Prosmotr/App.xaml.cs`
- Modify: `AGENTS.md`
- (обязательно) переопубликовать в `app\`

**Изменения:**

- `App.OnExit`: перед `_host.Dispose()`:

```csharp
// Останавливаем торрент-движок: стоп сессий, fast-resume (и удаление данных, если включено).
try { _host?.Services.GetService<ITorrentEngineService>()?.ShutdownAsync()
    .Wait(TimeSpan.FromSeconds(5)); } catch { }
```

- `AGENTS.md`: добавить раздел **5.36. Магнет-стриминг (MonoTorrent)** с подводными камнями:
  - streaming-режим — единственный штатный «последовательный» механизм (обычный режим —
    rarest-first, файл с дырами);
  - `prebuffer: true` решает MP4 с moov в конце;
  - `StreamProvider` — один поток на менеджер;
  - `StreamMediaInput` + блокирующее чтение: оверлей «докачивается…» по
    `TorrentStats.IsBeyondDownloaded`;
  - сессия закрывается при уходе с экрана; `CloseSession` держит данные (KeepAllData),
    кроме `DeleteTorrentCacheOnExit`;
  - magnet-протокол регистрируется в HKCU, тумблер off by default;
  - папка кэша `%LOCALAPPDATA%\Prosmotr\torrents\<hash>\data`.
- Публикация (обязательный финальный шаг, правило 3.1/3.2 — закрыть процессы, очистить кэш):

```bash
Get-Process -Name "Prosmotr" -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item -Path "app" -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish src/Prosmotr/Prosmotr.csproj -c Release -o app
```

- [ ] **Step 1: App.OnExit** — shutdown движка.
- [ ] **Step 2: AGENTS.md** — раздел 5.36.
- [ ] **Step 3: Полный цикл сборки и тестов**

```bash
dotnet build Prosmotr.sln -c Release
dotnet test tests/Prosmotr.Tests/Prosmotr.Tests.csproj
```

Expected: сборка 0/0, тесты зелёные (было 104, добавились ~20).

- [ ] **Step 4: Переопубликовать в `app\`** (команды выше).

---

### Task 12: Ручное интеграционное тестирование (E2E)

Прогнать на реальном магнете (легальный контент). Чек-лист:

- [ ] Запустить приложение из `app\Prosmotr.exe` → пустой экран → кнопка «Смотреть по магнет-ссылке…» → вставить ссылку → виден экран загрузки (название, %, скорость, пиры) → через ~10–30 с начинается воспроизведение.
- [ ] Перемотка назад — мгновенная; вперёд за границу скачанного — оверлей «Докачивается…», плеер не падает.
- [ ] Полный экран (кнопка) — работает; Esc — выход.
- [ ] Закрыть сессию (кнопка) → стартовый экран; повторно вставить ту же ссылку → загрузка продолжается (fast-resume), не с нуля.
- [ ] Вставить невалидную строку → тост «Неверная магнет-ссылка».
- [ ] Запустить приложение с `magnet:` ссылкой как аргументом (cmd: `app\Prosmotr.exe "<magnet>"`) → сразу открывается стриминг.
- [ ] В буфере обмена валидная ссылка → при старте открывается диалог с предзаполненной ссылкой.
- [ ] Настройки: включить «Открывать magnet-ссылки этим приложением» → клик по магнету в браузере открывает Просмотр.
- [ ] Настройки: «Удалять скачанное при выходе» = вкл → после выхода папка `<hash>` пуста.
- [ ] Закрытие окна во время загрузки — не падает, лог чистый (`%LOCALAPPDATA%\Prosmotr\app.log`).
- [ ] MP4 с moov в конце (обычный скачанный mp4, переупакованный без faststart) — начинает играть
  после prebuffer (первые+последние куски), а не висит на чёрном экране.

---

## Self-Review (делается после написания)

1. **Покрытие спеки:** каждый раздел спеки (входы, кэш, ошибки, настройки, magnet-протокол,
   shutdown, тесты) имеет задачу: Task 2–4 (логика), 5–6 (движок), 7–8 (UI), 9 (входы), 10
   (настройки), 11 (shutdown+docs+publish), 12 (E2E). Готово.
2. **Плейсхолдеры:** единственные «открытые» места — имена API, которые фиксирует спайк
   (Task 1 Step 4) — это осознанный де-риск, а не пропуск.
3. **Типы:** `TorrentSession`/`TorrentStatus`/`TorrentFileEntry`/`TorrentStats`/`TorrentCachePaths`
   определены один раз и используются консистентно во всех задачах. `Stream` в сессии —
   `System.IO.Stream`; `EngineRef` — `object?`.
