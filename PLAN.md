# План устранения недостатков — «Просмотр»

> Составлен на основе аудита кодовой базы + best practices .NET 8 / WPF / LibVLC.
> Каждый пункт содержит: проблема → место → действие → критерий приёмки.

---

## ✅ P0 — Критические (утечки памяти, стабильность, краши) — **ВЫПОЛНЕНО**

### ~~P0.1~~ Отписка от `Tick` во всех `DispatcherTimer` ✓
**Проблема:** `DispatcherTimer` держит strong reference на handler → на владельца. При пересоздании View/Window объекты не собираются GC.
**Где:**
- `MainWindow.xaml.cs` — `_chromeHideTimer`
- `VideoViewerView.xaml.cs` — `_hideTimer`, `_clickTimer`, `_pauseShowTimer`
- `VideoViewerViewModel.cs` — `_saveTimer`
**Действие:**
1. Во всех `Dispose()` / `OnUnloaded()` / `OnClosed()` добавить `timer.Tick -= Handler;` перед/после `Stop()`.
2. Для `MainWindow` (singleton) — отписка в `OnClosed` достаточна.
3. Для `VideoViewerView` (пересоздаётся) — обязательно в `OnUnloaded`.
**Критерий:** Профилирование dotTrace: после 50 переключений фото↔видео нет роста экземпляров `VideoViewerView` / `VideoViewerViewModel`.

---

### ~~P0.2~~ Убрать `DependencyPropertyDescriptor.AddValueChanged` из `VideoViewerView` ✓
**Проблема:** DPD хранит global strong reference на COM-связку «меню → code-behind». Динамические `ContextMenu` создаются каждый клик и никогда не отвязываются от DPD-реестра.
**Где:** `VideoViewerView.xaml.cs`, методы `OnSpeedButtonClick` / `OnAudioButtonClick` / `OnSubtitleButtonClick`
**Действие:**
1. Отказаться от `DependencyPropertyDescriptor` для отслеживания `IsOpen`.
2. Использовать событие `ContextMenu.Closed` напрямую (оно есть у `ContextMenu`).
3. Либо создать один reusable `ContextMenu` и обновлять `Items`, либо хранить ссылку и подписываться только на `.Closed +=` / `Closed -=` при показе.
**Критерий:** После 100 открытий меню скорости нет роста объектов `ContextMenu` в памяти.

---

### ~~P0.3~~ `async void` → `async Task` в `VideoViewerViewModel.FlashRateBadge` ✓
**Проблема:** `async void` не позволяет перехватить исключение вызывающему коду. Любой сбой внутри = краш приложения.
**Где:** `VideoViewerViewModel.cs`, метод `FlashRateBadge`
**Действие:**
```csharp
private async Task FlashRateBadgeAsync()
{
    ShowRateBadge = true;
    try { await Task.Delay(1200); } catch { /* Ignore */ }
    ShowRateBadge = false;
}
```
Вызвать через `_ = FlashRateBadgeAsync();` с комментарием «fire-and-forget, exceptions logged».
**Критерий:** Приложение не падает, если `Task.Delay` выбросит `ObjectDisposedException`.

---

### ~~P0.4~~ Заменить `dynamic` на строгие COM-интерфейсы в `ExplorerSortReader` ✓
**Проблема:** `dynamic` over COM в .NET 8 нестабилен: кэш CallSite растёт, возможны `RuntimeBinderException` в рантайме.
**Где:** `Infrastructure/ExplorerSortReader.cs`
**Действие:**
1. Определить `[ComImport]` интерфейсы `IShellWindows`, `IShellBrowser` (частично уже есть).
2. Заменить `dynamic windows = shellWindows` и `((dynamic)win).Document` на `IShellWindows` + `IShellFolderView`.
3. Убрать `UrlToPath` через `dynamic` — использовать прямой `LocationURL` через известный интерфейс.
**Критерий:** Работает на .NET 8 x64 без `Microsoft.CSharp` в депенденси (убрать, если был).

---

### ~~P0.5~~ Исправить apartment-поток для `ExplorerSortReader` ✓
**Проблема:** `await Task.Run(() => ExplorerSortReader.TryGetOrderedPaths(...))` запускает COM в MTA-потоке. Shell COM требует STA.
**Где:** `ViewModels/MainViewModel.cs`, метод `ResolveOrderingAsync`
**Действие:**
1. Либо вызывать `TryGetOrderedPaths` синхронно из UI-потока (он быстрый — только COM-enum).
2. Либо создавать dedicated STA-thread:
```csharp
var tcs = new TaskCompletionSource<List<string>>();
var thread = new Thread(() => { ... tcs.TrySetResult(paths); });
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
var paths = await tcs.Task;
```
**Критерий:** Нет `RPC_E_WRONG_THREAD` в логах при открытии папки из Проводника с нестандартной сортировкой.

---

### ~~P0.6~~ `CancellationTokenSource` в `ImageViewerViewModel` не освобождается ✓
**Проблема:** `_cts` создаётся при каждой загрузке, `Dispose()` никогда не вызывается. Утечка wait handle + callback-регистраций.
**Где:** `ViewModels/ImageViewerViewModel.cs`
**Действие:**
```csharp
public void Dispose() // добавить IDisposable
{
    _cts?.Cancel();
    _cts?.Dispose();
}
```
В `MainViewModel.UpdateCurrentContent` при замене `ImageViewerViewModel` вызывать `((IDisposable)oldImageVm)?.Dispose()`.
**Критерий:** `_cts` не фигурирует в утечках CLR Profiler после быстрой прокрутки галереи.

---

## P1 — Высокие (безопасность, архитектура, надёжность)

### P1.1 Атомарное сохранение поворота изображения
**Проблема:** `File.Copy(tmp, original, true)` + `File.Delete(tmp)` не атомарны. Сбой между операциями = потеря оригинала.
**Где:** `ViewModels/ImageViewerViewModel.cs`, `SaveRotationAsync`
**Действие:**
```csharp
File.Move(tmp, Item.FullPath, overwrite: true);
```
`File.Move` на одном томе атомарен (переименование).
**Критерий:** При принудительном убийстве процесса после `File.Move` файл остаётся валидным.

---

### P1.2 Убрать глотание `OutOfMemoryException` в `ImageDecodingService`
**Проблема:** `catch { return null; }` подавляет OOM, скрывая критическую нехватку памяти.
**Где:** `Services/ImageDecodingService.cs`
**Действие:**
```csharp
catch (OperationCanceledException) { throw; }
catch (OutOfMemoryException) { throw; }
catch { return null; }
```
**Критерий:** При OOM приложение логирует краш в `startup.log` и корректно завершается.

---

### P1.3 `CancellationToken` для `OpenPathAsync` / `BuildFromFolderAsync`
**Проблема:** Если пользователь открыл огромную папку и тут же другую — первая операция продолжает сканировать диск, блокируя IO.
**Где:** `MainViewModel.OpenPathAsync`, `Services/MediaLibraryService.cs`
**Действие:**
1. В `MainViewModel` добавить `private CancellationTokenSource? _openCts;`.
2. В начале `OpenPathAsync`: `_openCts?.Cancel(); _openCts = new CancellationTokenSource(); var ct = _openCts.Token;`
3. Передавать `ct` во все async-вызовы (`BuildFromFolderAsync`, `BuildFromFileAsync`).
4. В `MediaLibraryService` проверять `ct.ThrowIfCancellationRequested()` между файлами.
**Критерий:** IO падает до нуля в Resource Monitor сразу после повторного нажатия «Открыть папку».

---

### P1.4 Добавить `volatile` / `CancellationToken` для `_shuttingDown`
**Проблема:** Чтение `_shuttingDown` из фонового потока pipe-сервера без барьера памяти.
**Где:** `App.xaml.cs`
**Действие:**
```csharp
private volatile bool _shuttingDown;
```
Или заменить на `CancellationTokenSource _appCts`, передавая `Token` в `StartPipeServer`.
**Критерий:** При быстром `App.Shutdown()` pipe-сервер завершает цикл без лишней итерации.

---

### P1.5 Устранение зомби-STA-потоков в `FileDeletionService`
**Проблема:** При таймауте `IFileOperation` STA-поток остаётся висеть. При массовом удалении растёт число потоков.
**Где:** `Services/FileDeletionService.cs`
**Действие:**
1. Ограничить число попыток/таймаутов: если IFileOperation зависает 2 раза подряд — fallback на `SHFileOperation` или `File.Delete` + `FileSystem.DeleteFile` (Microsoft.VisualBasic).
2. Либо добавить `thread.Join(TimeSpan.FromSeconds(15))` после `Task.WhenAny`, и если не завершился — `thread.Interrupt()` (не `Abort` в .NET 8).
**Критерий:** После 10 удалений с имитацией зависания `IFileOperation` (mock) активных потоков не более 2.

---

### P1.6 DI-фабрики для дочерних VM вместо `new`
**Проблема:** `MainViewModel` создаёт `ImageViewerViewModel` и `VideoViewerViewModel` напрямую, нарушая IoC.
**Где:** `MainViewModel.UpdateCurrentContent`
**Действие:**
1. Зарегистрировать в `App.ConfigureServices`:
```csharp
services.AddTransient<Func<MediaItem, ImageViewerViewModel>>(sp =>
    item => new ImageViewerViewModel(item, sp.GetRequiredService<IImageCache>(), ...));
services.AddTransient<Func<MediaItem, VideoViewerViewModel>>(sp =>
    item => new VideoViewerViewModel(item, sp.GetRequiredService<LibVlcProvider>(), ...));
```
2. Внедрить `Func<MediaItem, ImageViewerViewModel> _imageVmFactory` в конструктор `MainViewModel`.
**Критерий:** `MainViewModel` не содержит `new ImageViewerViewModel(...)` / `new VideoViewerViewModel(...)`.

---

### P1.7 Изолировать UI-логику из `MediaItem`
**Проблема:** `MediaItem.FileSizeText` — форматирование в доменной модели.
**Где:** `Models/MediaItem.cs`
**Действие:**
1. Удалить свойство `FileSizeText` из `MediaItem`.
2. Создать `Converters/FileSizeConverter.cs` (`IValueConverter`), принимающий `long`.
3. Привязать `StatusText` через MultiBinding или формировать в `MainViewModel.UpdateStatus`.
**Критерий:** `MediaItem` содержит только `long FileSizeBytes`.

---

## P2 — Средние (производительность, UX, код-стайл)

### P2.1 Устранить двойное копирование памяти в `ImageDecodingService`
**Проблема:** MagickImage → PNG MemoryStream → BitmapImage. Лишний аллок + latency.
**Где:** `Services/ImageDecodingService.cs`, `LoadWithMagick`
**Действие:**
1. Проверить, доступен ли `MagickImage.ToBitmapSource()` в Magick.NET-Q8 14.x (есть в современных версиях).
2. Если нет — писать в `MemoryStream` с форматом `MagickFormat.Bmp` (быстрее, чем PNG) или использовать `Unsafe`/`Span` для копирования пикселей в `WriteableBitmap`.
**Критерий:** Открытие HEIC 24 Мп происходит быстрее на 20–30 %, пиковая память ниже.

---

### P2.2 Throttle / Debounce для `ZoomBorder.OnLayoutUpdated`
**Проблема:** `LayoutUpdated` вызывается при любом чихе визуального дерева, часто пересчитывая трансформы.
**Где:** `Views/Controls/ZoomBorder.cs`
**Действие:**
1. Добавить `DispatcherTimer _layoutDebounce` (30–50 мс).
2. В `OnLayoutUpdated` — `Restart()` таймера.
3. При срабатывании — вызывать `ApplyMode()`.
**Критерий:** Быстрый resize окна не вызывает >1 пересчёта `ApplyMode` за 50 мс.

---

### P2.3 Очередь для `ToastView`
**Проблема:** Новое уведомление мгновенно прерывает старое. Пользователь может пропустить сообщение.
**Где:** `Views/Controls/ToastView.xaml.cs`
**Действие:**
1. Добавить `Queue<NotificationRequest> _queue`.
2. `OnRequested`: enqueue → если не showing — `DequeueAndShow()`.
3. `Hide()` → `DequeueAndShow()`.
4. Добавить `MaxQueueLength` (например, 3), чтобы при спаме не копилось бесконечно.
**Критерий:** Три быстрых удаления подряд показывают три тоста последовательно.

---

### P2.4 Оптимизировать `SavePosition` (не каждую секунду)
**Проблема:** `DispatcherTimer` пишет JSON каждую секунду для каждого видео.
**Где:** `ViewModels/VideoViewerViewModel.cs`
**Действие:**
1. Убрать `_saveTimer`.
2. Сохранять позицию в `_positions` при событиях: `Paused`, `Stopped`, `EndReached`, `SeekTo`, `Dispose`.
3. Добавить `Window.Closing` / `App.OnExit` flush для гарантии.
**Критерий:** JSON пишется не чаще 1 раза за 5 секунд реального времени.

---

### P2.5 Увеличить `ImageCache` capacity
**Проблема:** 7 изображений — мало для современных систем.
**Где:** `Services/ImageCache.cs`
**Действие:**
```csharp
private const int Capacity = 24; // или динамически: Environment.WorkingSet-based
```
**Критерий:** Быстрая прокрутка 20 фото подряд не вызывает повторного декодирования.

---

### P2.6 Airspace: заменить `#01000000` на `#02000000`
**Проблема:** LibVLCSharp.WPF документация рекомендует alpha ≥ 2 для hit-test.
**Где:** `Views/VideoViewerView.xaml`
**Действие:**
```xml
<Grid x:Name="Overlay" Background="#02000000">
```
**Критерий:** Правый клик и клики по оверлею работают в High Contrast mode.

---

### P2.7 Touch / Pen поддержка в `ZoomBorder`
**Проблема:** `ZoomBorder` ловит только мышь. На планшете/convertible zoom/pan невозможны.
**Где:** `Views/Controls/ZoomBorder.cs`
**Действие:**
1. Подписаться на `ManipulationStarting`, `ManipulationDelta`, `ManipulationInertiaStarting`.
2. `IsManipulationEnabled = true`.
3. Pinch → zoom, Pan → translate.
**Критерий:** На Surface Pro pinch-to-zoom и pan работают для фото.

---

### P2.8 Валидация настроек `AppSettings`
**Проблема:** Все сеттеры публичные, нет валидации (например, `SeekStepSeconds = -1` или `SlideshowIntervalSeconds = 0`).
**Где:** `Models/AppSettings.cs`
**Действие:**
1. Добавить `System.ComponentModel.DataAnnotations`:
```csharp
[Range(1, 60)]
public int SlideshowIntervalSeconds { get; set; } = 4;
```
2. При загрузке JSON — fallback на default при невалидных значениях.
**Критерий:** Ручная правка `settings.json` с `"SeekStepSeconds": -5` приводит к fallback в 5.

---

## P3 — Низкие (рефакторинг, техдолг)

### P3.1 Разделить `MainViewModel` на под-контроллеры
**Где:** `ViewModels/MainViewModel.cs`
**Действие:**
- `GalleryController` — `OpenPath`, `HandleDrop`, сортировка.
- `NavigationController` — Next/Previous, индекс.
- `PresentationController` — FullScreen, Slideshow, ChromeVisible.
- `DeletionController` — Delete, Restore, Undo-state.

### P3.2 `IFileDeletionService` → return `OperationResult` вместо `bool`
**Где:** `Services/FileDeletionService.cs`
**Действие:**
```csharp
public record DeleteResult(bool Success, string? ErrorMessage);
```
Вместо `_notify.Show(...)` внутри сервиса — возвращать ошибку, пусть VM решает, показывать ли тост.

### P3.3 Унифицировать создание окон (DI vs `new`)
**Где:** `MainWindow.xaml.cs`, `OpenProperties`
**Действие:**
- `FilePropertiesWindow` должен создаваться через `IServiceProvider` (transient), как `SettingsWindow`.

### P3.4 Добавить `AutomationProperties.Name` к кнопкам видео-панели
**Где:** `Views/VideoViewerView.xaml`
**Действие:**
```xml
<ui:Button AutomationProperties.Name="Скорость воспроизведения" ... />
```

---

## Порядок работы (рекомендация)

### ✅ Выполнено
- **Фаза 0 (P0):** Все критические пункты — утечки памяти, стабильность, COM (`dynamic` → strict interfaces), STA-поток, `async void` → `Task`.

### 📋 Оставшиеся задачи
1. **Фаза 1 (P1 — высокие):** P1.1, P1.2, P1.3, P1.4, P1.5, P1.6, P1.7 — безопасность, архитектура, DI, надёжность.
2. **Фаза 2 (P2 — средние):** P2.1, P2.2, P2.3, P2.4, P2.5, P2.6, P2.7, P2.8 — производительность, UX, код-стайл.
3. **Фаза 3 (P3 — низкие):** P3.1, P3.2, P3.3, P3.4 — рефакторинг, техдолг.

> **Примечание по публикации:** после каждой итерации обязательно `dotnet publish src/Prosmotr/Prosmotr.csproj -c Release -o app` (см. `AGENTS.md` §3.1). Перед публикацией закрывать запущенные экземпляры.

---

> **Примечание по публикации:** после каждой итерации обязательно `dotnet publish src/Prosmotr/Prosmotr.csproj -c Release -o app` (см. `AGENTS.md` §3.1). Перед публикацией закрывать запущенные экземпляры.
