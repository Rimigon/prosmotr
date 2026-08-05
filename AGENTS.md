# AGENTS.md

> Автоматически поддерживаемая сводка контекста для AI-агентов.
> Последнее обновление: 2026-08-05 10:40:44 UTC

## Project Overview

<!-- agents-md:auto:project-overview -->
**Просмотр** — Нативное приложение в стиле «Фотографии» Windows 11, но быстрее и удобнее: единая галерея фото и видео, удобная навигация по папке, быстрое удаление в Корзину, полноценный видеоплеер с **глобальной скоростью воспроизведения** по умолчанию для всех видео.
<!-- /agents-md:auto:project-overview -->

## Tech Stack

<!-- agents-md:auto:tech-stack -->
Универсальный проект.
<!-- /agents-md:auto:tech-stack -->

## Setup Commands

<!-- agents-md:auto:setup-commands -->
- Добавьте команды установки и сборки вручную.
<!-- /agents-md:auto:setup-commands -->

## Development Workflow

<!-- agents-md:auto:development-workflow -->
- Опишите рабочий процесс вручную.
<!-- /agents-md:auto:development-workflow -->

## Testing Instructions

<!-- agents-md:auto:testing-instructions -->
- Команды тестирования не обнаружены — добавьте вручную.
- Перед коммитом убедитесь, что тесты проходят.
<!-- /agents-md:auto:testing-instructions -->

## Code Style

<!-- agents-md:auto:code-style -->
- Добавьте правила стиля кода вручную.
<!-- /agents-md:auto:code-style -->

## Build and Deployment

<!-- agents-md:auto:build-and-deployment -->
- Параметры сборки и деплоя не обнаружены — добавьте вручную.
<!-- /agents-md:auto:build-and-deployment -->

## Project Structure

<!-- agents-md:auto:project-structure -->
- `app/`
- `docs/`
- `src/`
- `tests/`
<!-- /agents-md:auto:project-structure -->

## Additional Notes

# AGENTS.md — памятка для AI-агентов и разработчиков

Документ описывает проект **«Просмотр»** и, главное, **неочевидные нюансы**, на которых легко
споткнуться. Перед изменениями прочитай разделы «Критичные нюансы» и «Подводные камни».

> Пользовательская документация — в [`README.md`](README.md). Здесь — то, что нужно знать,
> чтобы вносить изменения и проверять их правильно.

> 🔄 **Этот файл нужно держать актуальным.** При каждом изменении проекта обновляй `AGENTS.md`
> в рамках того же изменения. Подробнее — в разделе [«Соглашения по работе»](#6-соглашения-по-работе-важно).

---

## 1. Что это

Десктопный просмотрщик фото и видео для Windows 11 в стиле «Фотографии», но быстрее: единая
галерея фото+видео по папке, навигация, удаление в корзину, видеоплеер на LibVLC с глобальной
скоростью воспроизведения.

**Стек:**

- **WPF**, **.NET 8** (`net8.0-windows`), архитектура **MVVM**.
- **WPF-UI 4.3** — Fluent Design (тема, иконки `ui:SymbolIcon`, `FluentWindow`). Уведомления —
  НЕ через WPF-UI `Snackbar` (он не отрисовывался), а собственный `ToastView` (см. подводный камень 5.10).
- **CommunityToolkit.Mvvm 8.4** — `[ObservableProperty]`, `[RelayCommand]` (генераторы исходников).
- **Microsoft.Extensions.Hosting/DI 8** — DI-контейнер.
- **LibVLCSharp 3.9 + VideoLAN.LibVLC.Windows 3.0** — видео всех форматов без системных кодеков.
- **Magick.NET-Q8-AnyCPU 14** — декодирование WEBP/HEIC/HEIF.
- **XamlAnimatedGif 2.3** — анимированные GIF.

---

## 2. Команды

Из корня репозитория (`C:\Users\nikit\Рабочий стол\Просмотр`):

```powershell
# Сборка / запуск для разработки
dotnet build src\Prosmotr\Prosmotr.csproj -c Debug
dotnet run   --project src\Prosmotr\Prosmotr.csproj

# Публикация в папку app\ (см. критичный нюанс ниже!)
dotnet publish src\Prosmotr\Prosmotr.csproj -c Release -o app

# Юнит-тесты (чистая логика: навигация, сортировка, форматы, недавние)
dotnet test tests\Prosmotr.Tests\Prosmotr.Tests.csproj
```

- Решение: `Prosmotr.sln`. Платформа решения — Any CPU, но компилируется под **x64**
  (`<PlatformTarget>x64</PlatformTarget>` в csproj) — это обязательно для нативных плагинов LibVLC.
- Запуск с путём к файлу/папке как аргументом открывает его (используется ассоциациями Windows).

---

## 3. ⚠️ Критичные нюансы (читать обязательно)

### 3.1. Ярлык на рабочем столе запускает папку `app\`, а НЕ `bin\`

На рабочем столе есть ярлык **`Просмотр — фото и видео.lnk`**, который указывает на
**`app\Prosmotr.exe`** — это **отдельная опубликованная копия** приложения.

- Папка **`app/` в `.gitignore`** (не версионируется) — это локальный «дистрибутив».
- `dotnet build`/`dotnet run` собирают в `bin\Debug\...` или `bin\Release\...` —
  **ярлык их не видит**.
- **Если правки нужно увидеть в приложении, запускаемом ярлыком, — переопубликуй в `app\`:**

  ```powershell
  dotnet publish src\Prosmotr\Prosmotr.csproj -c Release -o app
  ```

  (предварительно закрой запущенные экземпляры — иначе `Prosmotr.exe`/`.dll` заблокированы).
- Скомпилированный XAML (BAML) лежит внутри `Prosmotr.dll`, поэтому правки в `.xaml` тоже требуют
  переиздания/пересборки, а не только копирования ресурсов.

> Если пользователь говорит «изменений нет / кнопки нет», первым делом проверь, **что именно он
> запускает** (ярлык → `app\`), и переопубликуй туда.

### 3.2. Перед проверкой изменений: закрыть процессы и очистить кэш

WPF + LibVLC агрессивно кэшируют нативные сборки, ресурсы XAML/BAML и плагины VLC. Чтобы
изменения в коде/XAML точно попали в запускаемое приложение и не «прилипали» старые версии:

1. **Закрой все процессы `Prosmotr.exe`** (включая зависшие/фоновые):  

   ```powershell
   Get-Process -Name "Prosmotr" -ErrorAction SilentlyContinue | Stop-Process -Force
   ```

2. **Полностью очисти папку `app\`** (то, что видит ярлык):  

   ```powershell
   Remove-Item -Path "app" -Recurse -Force -ErrorAction SilentlyContinue
   ```

3. **Очисти кэш сборки** (`bin`/`obj`), чтобы не осталось stale BAML/ресурсов:  

   ```powershell
   Remove-Item -Path "src\Prosmotr\bin","src\Prosmotr\obj" -Recurse -Force -ErrorAction SilentlyContinue
   Remove-Item -Path "tests\Prosmotr.Tests\bin","tests\Prosmotr.Tests\obj" -Recurse -Force -ErrorAction SilentlyContinue
   ```

4. **Очисти временные файлы приложения в `%TEMP%`** (заблокированные DLL/lock-файлы .NET):  

   ```powershell
   Remove-Item -Path "$env:TEMP\Prosmotr*" -Recurse -Force -ErrorAction SilentlyContinue
   Remove-Item -Path "$env:TEMP\\.NET*" -Recurse -Force -ErrorAction SilentlyContinue
   ```

5. Сразу после очистки пересобери и переопубликуй в `app\` (иначе кэш/lock-файлы могут
   восстановиться при промежуточных действиях):  

   ```powershell
   dotnet publish src\Prosmotr\Prosmotr.csproj -c Release -o app
   ```

> Без шагов 1–4 нередко возникает эффект «я только что исправил, а изменений нет»: процесс
> держит старые DLL, в `app\` остаются устаревшие плагины LibVLC или BAML из `obj\`.
> Очистку и публикацию выполняй как единый непрерывный шаг.

### 3.3. Single-file publish ЗАПРЕЩЁН

Single-file ломает загрузку нативных плагинов LibVLC. Публикуй обычным способом (framework-dependent).
После сборки в `…\libvlc\win-x64\` должны лежать `libvlc.dll`, `libvlccore.dll` и папка `plugins\`.
Это отражено комментарием в `Prosmotr.csproj` и в README.

### 3.4. Только x64

Нативные плагины LibVLC грузятся из `libvlc\win-x64`. Процесс обязан быть 64-битным
(`PlatformTarget=x64`, `Prefer32Bit=false`). Не переключай на x86/AnyCPU-32.

### 3.5. Single-instance на уровне папки

В `App.xaml.cs`: при запуске вычисляется папка открываемого пути (для файла — его каталог,
для папки — она сама). На каждую папку создаётся свой `Mutex` (`Prosmotr.SingleInstance.<hash>`)
и named pipe (`Prosmotr.OpenFile.<hash>`). Если для этой папки уже запущен экземпляр —
новый процесс отправляет путь работающему через pipe и завершается. Если папка новая —
открывается **новое окно**.

Таким образом:

- файлы из одной папки открываются в одном и том же окне;
- файлы из разных папок открываются в разных окнах;
- пустой запуск (без аргументов) использует отдельный мьютекс `empty`, пока не откроет первую папку.

При отладке закрывай все экземпляры, если хочешь сбросить привязку к папке.

---

## 4. Архитектура

Классический **MVVM** поверх DI-контейнера (`Microsoft.Extensions.Hosting`).

- **Точка входа** — `App.xaml.cs`: строит `IHost`, регистрирует сервисы и VM в `ConfigureServices`,
  показывает `MainWindow`, применяет тему, разбирает аргументы, single-instance, интеграция с shell.
- **`MainViewModel`** — главный оркестратор: открытие файлов/папок, навигация, удаление,
  полноэкранный режим, слайд-шоу, сортировка. Держит `CurrentContent` (объект-VM текущего экрана).
  Реализован как `partial class` и разбит на 5 файлов:
  `MainViewModel.Gallery.cs` (открытие, сортировка), `MainViewModel.Navigation.cs` (переключение),
  `MainViewModel.Presentation.cs` (FullScreen, Slideshow), `MainViewModel.Deletion.cs` (Delete/Restore),
  `MainViewModel.FileActions.cs` (проводник, свойства, буфер обмена).
- **Контент-экраны** выбираются по типу VM в `CurrentContent`, отрисовываются `ContentControl`
  в `MainWindow.xaml` через неявные `DataTemplate` (тип VM → View):
  - `EmptyStateViewModel` → `EmptyStateView` (стартовый экран, когда ничего не открыто);
  - `ImageViewerViewModel` → `ImageViewerView` (фото/GIF);
  - `VideoViewerViewModel` → `VideoViewerView` (видео, оверлей поверх VLC).
- **Сервисы** (все — singletons, см. `App.ConfigureServices`) с интерфейсами в
  `Services/Abstractions/`: библиотека медиа, навигация, удаление (`IFileDeletionService` → `DeleteResult`),
  настройки, тема, кэш изображений, декодирование, миниатюры, позиции видео, ассоциации файлов,
  shell-операции, провайдер LibVLC, **топология дисплеев** (`IDisplayTopologyService`/`DisplayTopologyService`),
  **уведомления** (`INotificationService`/`NotificationService`).
- **DI-фабрики для дочерних VM.** `MainViewModel` не создаёт `ImageViewerViewModel` и
  `VideoViewerViewModel` напрямую через `new`, а получает `Func<MediaItem, ImageViewerViewModel>`
  и `Func<MediaItem, VideoViewerViewModel>` из контейнера (зарегистрированы в `App.ConfigureServices`).
- **Уведомления.** `NotificationService` лишь поднимает событие `Requested` в UI-потоке; рисуют тост
  сами View — контрол `ToastView` (в главном окне и **внутри оверлея видео**, чтобы тост был виден
- **Уведомления.** `NotificationService` лишь поднимает событие `Requested` в UI-потоке; рисуют тост
  сами View — контрол `ToastView` (в главном окне и **внутри оверлея видео**, чтобы тост был виден
  поверх airspace VLC). `ToastView` получает `INotificationService` через публичное свойство
  `MainViewModel.NotificationService` (окно передаёт DataContext, `ToastView` берёт сервис из него).
  **Не** обращайся к `Application.Current as App` — Service Locator устранён (P3).
  **Очередь:** `ToastView` держит `Queue<NotificationRequest>` (max 3). Новое уведомление не
  прерывает текущее мгновенно — встаёт в очередь и показывается после скрытия предыдущего.

### Карта каталогов

```
src/Prosmotr/
  App.xaml(.cs)              — вход, DI, single-instance, ассоциации
  app.manifest               — DPI awareness PerMonitorV2, supportedOS, longPathAware
  Prosmotr.csproj            — TFM, x64, пакеты, запрет single-file
  Models/                    — MediaItem, AppSettings, RecentEntry, перечисления (MediaType, SortField…)
  Services/                  — реализации сервисов
    Abstractions/            — интерфейсы сервисов (IxxxService), включая IDisplayTopologyService
    VideoFramePreviewService — второй «скрытый» декодер для превью кадра при наведении на таймлайн
                               (создаётся самим VideoViewerViewModel, НЕ singleton в DI; см. 5.33)
  ViewModels/                — по VM на экран + MainViewModel (partial: Gallery, Navigation,
                               Presentation, Deletion, FileActions), ViewModelBase, Messages
  Views/                     — MainWindow, EmptyStateView, ImageViewerView, VideoViewerView,
                               ThumbnailStripView, SettingsWindow, FilePropertiesWindow (окно «Свойства»)
    Controls/ZoomBorder.cs   — кастомный контрол зума/панорамы для фото (см. подводные камни)
    Controls/ToastView       — всплывающие уведомления (тост), см. подводный камень 5.10
  Converters/                — конвертеры XAML (BoolToVis, InverseBoolToVis, …)
  Infrastructure/            — AppLog, SupportedFormats, NativeMethods (Корзина),
                               RecycleBinRestore (отмена удаления), ShellThumbnail,
                               ExplorerSortReader, NaturalStringComparer,
                               ShellMetadata (длительность видео через Shell-COM),
                               FullScreenHelper (Win32 borderless fullscreen),
                               DisplayConfigApi (CCD: QueryDisplayConfig / SetDisplayConfig P/Invoke)
  Resources/                 — иконка app.ico, темы (AppResources.xaml)
app/                         — ⚠️ опубликованная копия (в .gitignore), на неё ведёт ярлык
tests/Prosmotr.Tests/        — xUnit-тесты чистой логики (net8.0-windows, x64): NavigationService,
                               MediaLibraryService.Sort (+ StableSort), SupportedFormats,
                               NaturalStringComparer, RecentFilesService. НЕ грузят нативы
                               LibVLC/Magick и не поднимают WPF Application — быстрые, headless.
```

---

## 5. Подводные камни (gotchas)

### 5.1. Фото в `ZoomBorder`: содержимое в `Canvas`, не в `Grid`

`ImageViewerView.xaml` помещает `<Image Stretch="None">` внутрь **`Canvas`** внутри `ZoomBorder`.
`ZoomBorder` (наследник `Border`) масштабирует/двигает содержимое через `RenderTransform`
(scale + translate), вычисляя масштаб от **натурального размера** `Image.Source.Width/Height`.

- **Почему именно `Canvas`:** при `Stretch="None"` `Image` сообщает натуральный размер. Если
  поместить его в `Grid`, родитель выделяет ему layout-слот размером с окно, и WPF накладывает
  **layout-clip** — большое изображение обрезается до левого-верхнего фрагмента ещё до трансформа.
  `Canvas` меряет детей бесконечным размером и не клипует → картинка рендерится целиком, а
  вписывание делает трансформ. **Не меняй `Canvas` обратно на `Grid`** — вернётся обрезка фото.
- `GetNaturalContentSize` в `ZoomBorder` рекурсивно ищет первый видимый `Image` с `Source` и
  учитывает `LayoutTransform` (поворот). Режимы: `Fit` (вписать), `ActualSize` (100%), `Fill` (заполнить).
- `ImageViewerView.xaml.cs` при смене фото сначала **синхронно** скрывает содержимое
  (`ZoomBorder.HideContent()`), а затем на `DispatcherPriority.Render` сбрасывает и пересчитывает
  зум (`ResetContent(Fit)` + `SetMode(Fit)`). Содержимое остаётся невидимым (`Opacity = 0`),
  пока `ApplyMode()` не получит реальные размеры нового `Image.Source` — это устраняет
  мелькание кадра со старым масштабом/положением.
- **Раскрытие содержимого — по любому успешному `ApplyMode`.** Раньше раскрытие зависело от
  флага `_pendingReveal`; при синхронном кэш-хите флаг мог сброситься раньше фактической смены
  `Image.Source`, и фото оставалось невидимым (чёрный экран). Теперь `ZoomBorder.ApplyMode`
  выставляет `Opacity = 1` при любом успешном расчёте размеров, независимо от флага.
- **Страховка от «залипшего» чёрного экрана.** `ImageViewerView` дополнительно отслеживает
  изменение `Image.Source` у `StaticImage`/`AnimatedImage` через `DependencyPropertyDescriptor`
  и вызывает `Zoom.SetMode(Fit)` после фактической смены источника. Это ловит случаи,
  когда `PropertyChanged` VM возник до привязки обработчика (синхронный кэш-хит при
  переиспользовании View) или `DispatcherPriority.Render` отработал раньше binding'а.
- **`LayoutUpdated` throttled.** `ZoomBorder` держит `DispatcherTimer` с интервалом **40 мс**:
  каждый `LayoutUpdated` лишь **рестартует** таймер, а `ApplyMode()` вызывается только после
  затихания событий. Это предотвращает лишние пересчёты при быстром resize окна.
- **Touch / pinch-to-zoom.** `IsManipulationEnabled = true`. `ManipulationDelta` разбирает
  `DeltaManipulation.Scale` (pinch → `ZoomAt`) и `DeltaManipulation.Translation` (pan →
  сдвиг `_translate`). Работает на планшетах / Surface без стилуса.
- **При потере захвата мыши (`OnLostMouseCapture`) сбрасывается `_dragging`.** Если во время
  перетаскивания курсор уходит за пределы окна, `OnMouseLeftButtonUp` не вызовется, а
  `OnLostMouseCapture` — да. Без сброса флага изображение продолжит «прилипать» к курсору
  при возвращении мыши в окно.

### 5.2. Стартовый экран (`EmptyStateView`) обязан прокручиваться

Контент центрирован по вертикали. Список «Недавние» (до 8 записей) + кнопки могут не влезть по
высоте, и нижние элементы (кнопка «Очистить историю») уходят за край окна. Поэтому контент обёрнут
в `ScrollViewer` с приёмом **`MinHeight = ViewportHeight`** (через `RelativeSource` к `ScrollViewer`):
пока влезает — центрирование сохраняется, когда не влезает — появляется прокрутка. Сохраняй этот
приём при правках стартового экрана.

Блок «Недавние» виден только при `HasRecent == true`. Кнопка очистки привязана к
`ClearRecentCommand` (генерируется из `[RelayCommand] ClearRecent` в `EmptyStateViewModel`).

### 5.3. `ContentControl` переиспользует View при смене фото

При переходе между фото тип VM один и тот же (`ImageViewerViewModel`), поэтому `ContentControl`
**не пересоздаёт** `ImageViewerView`, а лишь меняет `DataContext`. Поэтому переинициализация во
View висит на `DataContextChanged`, а не только на `Loaded` (см. `ImageViewerView.xaml.cs`).

### 5.4. Видео→видео переиспользует тот же плеер

В `MainViewModel.UpdateCurrentContent`: если и старый, и новый элемент — видео, вызывается
`VideoViewerViewModel.SwitchTo(...)` на существующем VM (тот же плеер/окно) — без рывков и без
пересоздания оверлейного окна. Предыдущий контент (видео) освобождается через `IDisposable`
с задержкой (`DispatcherPriority.Background`) после визуальной замены.

#### Перемотка таймлайна дросселируется (плавный seek без артефактов)

`VideoViewerView`: `Slider` таймлайна генерирует `ValueChanged` непрерывно во время
перетаскивания «бегунка». Если на каждое такое событие сразу звать `VideoViewerViewModel.SeekTo`
(а он делает `Player.Time = …`), декодер VLC захлёбывается потоком seek'ов — видимые лаги и
артефакты (кадры декодируются от ключевых и накладываются). Поэтому:

- Перетаскивание ловим через routed-события `Thumb.DragStarted/DragCompleted` (всплывают к Slider),
  флаг `_isSeekDragging`.
- Во время drag перемотки **дросселируются** таймером `_seekThrottle` (~120 мс): копим последнюю
  целевую позицию (`_pendingSeekMs`) и перематываем не чаще раза в интервал.
- При отпускании (`DragCompleted`) — одна финальная **точная** перемотка на отпущенную позицию.
- Клик по дорожке (`IsMoveToPointEnabled`) — это не drag → перематываем сразу, одиночным seek.
- Во время drag событие `PositionMs` от плеера **игнорируется** (`if (_isSeekDragging) return`),
  чтобы ползунок не «прыгал» под курсором.
Таймер останавливается и флаги сбрасываются в `OnUnloaded`.

#### Клавиатурные шаги ←/→: поколение seek'а отбрасывает устаревшие `TimeChanged`

`VideoViewerViewModel.StepForward/Backward` накапливают направления за 80 мс (`_stepThrottle`)
и выполняют один seek. После seek'а `OnTimeChanged` игнорируется первые 180 мс (`_seekCooldown`),
чтобы промежуточная позиция от декодера не перезаписала `PositionMs`. Однако `TimeChanged`
приходит из потока LibVLC и маршалится в UI через `BeginInvoke`; устаревшее событие может
выполниться уже после окончания cooldown и всё равно вернуть ползунок/счётчик назад. Поэтому
добавлено монотонное поколение seek'а (`long _seekGen`): в `SeekTo` инкрементируется
`Interlocked.Increment(ref _seekGen)`, а в `OnTimeChanged` захватывается номер поколения на
момент события и сравнивается с текущим; событие из предыдущего поколения отбрасывается
независимо от времени выполнения.

**Position-based guard «держим ползунок на цели, пока декодер не пройдёт её».**
`libvlc_media_player_set_time` делает **accurate seek**: декодер приземляется на
ближайший ключевой кадр K≤цели и **разгоняется** K→цель (декодирует промежуточные
кадры). На видео с длинным GOP (напр. запись экрана — ключевые кадры каждые ~12 с,
подтверждено ffprobe) этот разгон длится дольше 180 мс cooldown'а, и `TimeChanged`
сообщает позиции **НИЖЕ цели** — без guard'а `PositionMs` (а с ним ползунок/счётчик)
прыгает назад к ключевому кадру (тот самый дефект «перемотка прыгает обратно» на
«старых» видео). **Нюанс:** `set_time(T)` сперва возвращает **ЭХО** `e.Time==T` ещё до
реальной перемотки — поэтому условие отпускания не `e.Time≥T` (сработало бы на эхе,
и последующие разгонные события ниже цели переписали бы `PositionMs` назад), а строго
`e.Time > T`. При **обратной** перемотке декодер сперва шлёт **СТАРУЮ** позицию (выше
цели) — её ловит проверка «рядом с якорем».

Поэтому в `OnTimeChanged` после cooldown/поколения стоит guard: при seek'е
запоминаются «якорь» (`_seekAnchorMs` — позиция ДО seek'а) и цель (`_seekTargetMs`);
пока событие **рядом с якорем** (`|e.Time − якорь| < 500 мс` — ловит старую позицию при
обратной перемотке) **ИЛИ не прошло цель** (`!(e.Time > цель && !nearOld)` — эхо `==цели`,
разгон `<цели`, старая позиция) — `PositionMs` удерживается на цели, событие
отбрасывается. Guard снимается **только когда декодер ПРОШЁЛ цель** (`e.Time > цель` и
ушёл от якоря) — `PositionMs` трекается нормально. `PositionMs` ставится на цель в
`SeekTo` **и для drag, и для клавиатурного seek'а** (во время перетаскивания View
игнорирует `PositionMs` через `_isSeekDragging`, так что это безопасно). Жёсткий потолок
`SEEK_GUARD_S = 10 с` — страховка от вечного зависания, если seek не удался (декодер
так и не дошёл); в норме никогда не срабатывает, т.к. catchup происходит за <1–5 с даже
на длинном GOP под софтверным декодером. **Отпускание по факту catchup, а не по
таймеру** — короткое окно (2 с) на длинном GOP приводило к `release-safety` ниже цели и
прыжку ползунка назад (TRACK-BACK). События с `e.Time < 0` (стоп/выгрузка медиа шлёт
`t=-1`) отбрасываются целиком — иначе `PositionMs` уходил в минус и ползунок падал
в начало. Якорь/цель сбрасываются при новой загрузке (`LoadAndPlayDeferred`) и в
`OnEndReached` (иначе отложенные шаги ждали бы guard вечно — после стопа `TimeChanged`
не приходит). Не убирай этот guard — одни time-based cooldown'ы (180 мс) не покрывают
длинный GOP.

**Seek из `EndReached` — только назад.** Ветка reload (перезагрузка дорожки через
`:start-time`, т.к. на остановленном плеере `SetTime+Play` ненадёжен) срабатывает только
для **обратной** перемотки (`clamped < PositionMs - 100`). Перемотка **вперёд** у/за конца
при уже законченном видео — no-op (раньше reload срабатывал для любого `clamped < LengthMs`,
и удержание стрелки вперёд у конца бесконечно воспроизводило последний сегмент:
reload → `EndReached` → reload …). К следующему файлу пользователя переносит обычная
навигация стрелками в `MainWindow`, а не этот seek.

#### Освобождение плеера при уходе с видео

Когда `VideoViewerView` удаляется из дерева (видео → фото / пустой экран / закрытие окна),
`OnUnloaded` вызывает `_vm?.StopAndRelease()` до `Detach()`. Это немедленно останавливает
воспроизведение и освобождает `Media`, чтобы нативное окно LibVLC не оставалось «висеть»
поверх WPF и не держало handle удаляемого/закрываемого файла. Финальный `Dispose` VM всё равно
отработает через `_pendingDisposal` (`MainViewModel` / `App.OnExit`).

#### Чёрный cover при загрузке первого кадра (белый квадрат при переключении видео)

`LibVLCSharp.WPF.VideoView` рендерит видео в нативное Win32-окно (`VideoHwndHost` — `HwndHost`
класса `"static"` с `WS_EX_TRANSPARENT`), поверх которого плавает прозрачный `ForegroundWindow`
с WPF-оверлеем. При смене медиа (`SwitchTo` → `Player.Media = new; Play()`) между остановкой
старого vout и отрисовкой первого кадра нативное окно закрашивается своей фоновой кистью
класса `"static"` (светлой/белой) — отсюда **белый квадрат** при переключении видео. WPF-фон
`VideoHost`/`VideoView` этого **не лечит**: он находится **за** непрозрачным `HwndHost`
(поэтому правка «чёрный фон VideoHost» убрала лишь белые *полосы по краям*, но не сам *квадрат*).
`:start-paused` уменьшает вспышку, но не убирает — есть окно до первого кадра.

Решение — чёрный `Border x:Name="SwitchCover"` в оверлее (`Grid.Row="0"`, `Grid.RowSpan="2"`,
позже всех элементов, кроме `ToastView`): оверлей едет в `ForegroundWindow`, который всегда
поверх нативного HWND, поэтому opaque-чёрный cover гарантированно перекрывает белый.

- `VideoViewerViewModel.IsBuffering` (`[ObservableProperty]`): `true` в конструкторе (свежий
  VM вот-вот грузит), в `BeginPlayback` (старт/`SwitchTo`, в самом начале) и в `Replay`;
  `false` в `OnPlaying` (первый кадр готов — с `:start-paused` он отрисован до события `Playing`),
  `OnError` (чтобы видна была плашка ошибки) и `Dispose`.
- **Load/Play отложены до отрисовки cover'а (критично!).** `BeginPlayback`/`Replay` не зовут
  `_playback.Load`/`Play` синхронно, а через `LoadAndPlayDeferred` — `Dispatcher.BeginInvoke(
  Background)`. `Background` выполняется **после** `Render` (паттерн как `MainViewModel` для
  «после визуальной замены»), поэтому WPF успевает закрасить чёрный cover поверх нативного HWND
  ДО того, как смена `Media`/`Play` заставит его мигнуть белым. Без этой отсрочки cover
  ставился Visible синхронно, но WPF красил его лишь на следующем render-цикле — нативное окно
  успевало мигнуть раньше (белый квадрат/полосы оставались). Поле `_loadGen` (поколение).guard'ит
  устаревшие отложенные загрузки при быстрой навигации: отрабатывает только последняя SwitchTo/Replay.
- **MediaPlayer тоже привязывается после Render.** В `OnDataContextChanged`/`OnLoaded` сначала
  поднимается cover (`UpdateCover`), затем `Video.MediaPlayer = ...` и `vm.Start()` ставятся в
  `Dispatcher.BeginInvoke(..., DispatcherPriority.Render)`. Иначе смена `MediaPlayer` в LibVLC
  могла заставить нативное окно мигнуть ещё до того, как cover отрисуется.
- `VideoViewerView.UpdateCover()` синхронизирует `SwitchCover.Visibility` с `IsBuffering`;
  вызывается из `OnVmPropertyChanged` (по изменению) и при привязке VM (`OnDataContextChanged`,
  `OnLoaded`) — чтобы свежий VM, уже лежащий в `IsBuffering=true`, сразу показал cover
  (PropertyChanged на уже-true значение при подписке не приходит).
- **Панель управления скрывается на время буферизации.** `UpdateCover()`/`UpdateChromeVisibility()`
  прячут `ControlBar`, боковые стрелки и инфо-плашку, пока `IsBuffering == true`. Иначе
  полупрозрачная панель (`#D91A1A1A`) пропускала бы светлый фон нативного HWND в нижней части
  экрана, и пользователь видел «белые квадраты» именно там. После готовности первого кадра
  видимость панели восстанавливается по `_controlsShown`/`AutoHideControls`.
- **Удаление видео в полноэкранном режиме.** `MainViewModel.Delete` перед `StopAndRelease()`
  устанавливает `videoVm.IsBuffering = true`. Без этого cover поднимался только в `SwitchTo`,
  а между остановкой старого плеера (очистка HWND LibVLC → белый фон) и переключением на
  следующий файл весь экран на секунду становился белым. Cover в оверлее закрывает этот
  промежуток, как при обычном переключении видео.

### 5.5. Декодирование изображений: нативный WPF vs Magick

`ImageDecodingService`: WEBP/HEIC/HEIF **и JPEG** (`SupportedFormats.RequiresMagick`) декодируются
через Magick.NET (конвертация в **BMP** в памяти — раньше был PNG, но BMP не требует сжатия и
быстрее), остальное — нативным WPF (`BitmapImage` + `OnLoad`, синхронно, чтобы `Width/Height` были
доступны сразу). JPEG намеренно отправлен через Magick.NET, т.к. встроенный декодер WPF падает
(`ArgumentException` в `ColorContext.GetColorContextsHelper`) на некоторых JPG с embedded
ICC-профилями. Magick нормализует/сбрасывает проблемные профили перед отдачей WPF.
Анимированные GIF рисует **не** этот сервис, а XamlAnimatedGif прямо во View
(`AnimationBehavior.SourceUri`).

- **Удаление GIF требует освобождения handle.** XamlAnimatedGif держит `FileStream` файла
  открытым во время анимации, поэтому `IFileOperation` не может переместить GIF в Корзину
  (`aborted=True`). Перед удалением `MainViewModel.Delete` через
  `ImageViewerViewModel.RequestReleaseFileHandle()` сбрасывает `SourceUri` на `null`;
  при неудачном удалении `RequestRestoreFileHandle()` восстанавливает binding.
- **Показ GIF — через событие `Loaded` XamlAnimatedGif + явный `SetSourceUri`.**
  `DependencyPropertyDescriptor` на `Image.Source` анимированного GIF давал
  множественные/задержанные уведомления, из-за чего GIF оставалась невидимой
  после готовности первого кадра. Кроме того, attached-property binding
  `AnimationBehavior.SourceUri` в XAML плохо перезагружается при переиспользовании
  `ImageViewerView` (ContentControl меняет только DataContext), поэтому второй и
  последующие GIF открывались в чёрный экран. В `ImageViewerView`:
  - `SourceUri` задаётся явно из code-behind в `OnDataContextChanged`
    (`SetSourceUri(null)`, затем `SetSourceUri(vm.AnimatedSource)`);
  - подписка на `AnimationBehavior.LoadedEvent` вызывает `Zoom.SetMode` при
    готовности первого кадра;
  - при отсоединении VM `SourceUri` сбрасывается, а `AnimatedImage.Source = null`
    завершает Animator синхронно.

`ImageCache` — LRU полноразмерных изображений (`Capacity = 24`; раньше 7) для мгновенного
переключения между соседними фото (`Preload` соседей). Полный размер = `decodePixelWidth=0`.

### 5.6. Порядок галереи: приоритет источников сортировки

`MainViewModel.ResolveOrderingAsync`: (1) явный выбор пользователя для конкретной папки
(`ManualFolderSorts` в настройках) → (2) реальный порядок открытого окна Проводника
(`ExplorerSortReader`, если включено `MatchExplorerSort` — **по умолчанию выключено**, см. ниже) →
(3) глобальная настройка `SortBy`. Таким образом, по умолчанию программа всегда применяет
собственную сортировку, сохранённую внутри приложения, а не порядок Проводника. Опция
«Порядок как в открытом окне Проводника» в настройках включает ветку (2) для тех, кому она нужна.
Изменение сортировки в UI запоминается для текущей папки и перекрывает Проводник в следующий раз.

`MainViewModel.ReflectSort` устанавливает UI-индикатор (`SelectedSortField`/`SortDescending`)
и **явно вызывает `ApplySort()`**. Это гарантирует, что при открытии новой папки сортировка
применена, даже если индикатор уже находился в нужном состоянии и событие изменения не
сработало (например, после перезапуска приложения).

**Поле «Продолжительность» (`SortField.Duration`).** Фото и видео сортируются
раздельно по двум группам. По возрастанию: сначала фото (по размеру файла),
затем видео (по длительности). По убыванию: сначала видео (по длительности),
затем фото (по размеру файла). Внутри каждой группы применяется выбранное
направление сортировки, а при равенстве ключей используется натуральное имя
файла. Реализовано в `MediaLibraryService.CompareDuration`.

Длительность видео НЕ хранится в `MediaItem` по умолчанию и **читается лениво** через
Shell-метаданные `System.Media.Duration` (`Infrastructure/ShellMetadata.TryGetDurations`,
один `NameSpace` на папку, STA-поток `StaTask` — Shell-COM апартмент-ниточный, как
`ExplorerSortReader`/`RecycleBinRestore`). Чтение довольно дорогое (COM на файл), поэтому
оно происходит **только когда выбрана сортировка по продолжительности**: при открытии
папки с такой сортировкой — внутри `ScanAsync` (`MediaLibraryService.EnsureDurationsAsync`
перед `ApplyOrder`); при смене поля на «Продолжительность» уже открытой папки — внутри
`MainViewModel.ApplySortAsync` (поэтому `ApplySort` стал async, остальные поля идут
синхронно без await). `EnsureDurationsAsync` идемпотентен (не перечитывает уже
заполненные `DurationMs`). Не вычитанные/неизвестные длительности = 0 — такие видео идут
первыми по возрастанию. Не добавляй чтение длительности в каждое сканирование — это
замедлит открытие видео-тяжёлых папок при сортировке по имени/дате.

### 5.7. Хранилище данных и логи

- `%APPDATA%\Prosmotr\settings.json` — настройки + **недавние файлы** (`RecentFiles`). Атомарная
  запись (tmp + `File.Replace`), сохранение дебаунсится (`SaveDebounced`, 800 мс).
- `%LOCALAPPDATA%\Prosmotr\positions.json` — позиции воспроизведения видео (resume).
- `%LOCALAPPDATA%\Prosmotr\app.log` — диагностика (`AppLog.Write`); `startup.log` — краши запуска.
- При диагностике можно временно добавлять `AppLog.Write(...)` и читать `app.log` — так в этой
  кодовой базе удобно подтверждать поведение раскладки/размеров (не забудь убрать диагностику).

### 5.8. DPI и длинные пути

`app.manifest` включает **PerMonitor V2 DPI awareness** и `longPathAware`. Размеры в WPF — в DIP;
учитывай это, если работаешь с пиксельными размерами изображений (`BitmapSource.Width` в DIP, а
`PixelWidth` — в пикселях).

### 5.9. Интеграция с Windows

`FileAssociationService` регистрирует ProgID в **HKCU** (пункт «Открыть с помощью»); при каждом
запуске путь в реестре переписывается на актуальный `exe` (`TryIntegrateShell`). Windows 11 не даёт
программно назначать приложение по умолчанию — только вручную через системные настройки.

### 5.10. Уведомления, свойства файла и восстановление из Корзины

- **Тосты — свои, не WPF-UI `Snackbar`.** WPF-UI `SnackbarService`/`SnackbarPresenter` в этой сборке
  **не отрисовывался** (вызов `Show` корректен, но плашка не появлялась). Заменён на собственный
  `Views/Controls/ToastView` + `INotificationService`. Не возвращай Snackbar. `ToastView` есть и в
  `MainWindow`, и внутри оверлея `VideoViewerView` — иначе тост не виден поверх видео (airspace VLC).
  **Очередь:** `ToastView` держит `Queue<NotificationRequest>` (max 3). Новое уведомление не
  прерывает текущее мгновенно — встаёт в очередь и показывается после скрытия предыдущего.
- **Свойства файла — собственное окно `FilePropertiesWindow`** (в стиле приложения, FluentWindow),
  а НЕ системный диалог Windows. `MainViewModel.ShowProperties` поднимает событие `PropertiesRequested`,
  `MainWindow.OpenProperties` показывает окно. `FilePropertiesViewModel` берёт базовые поля из
  `MediaItem`, размеры фото — через `MagickImageInfo`, разрешение/длительность/FPS видео — через
  `Media.Parse` LibVLC (поэтому окну нужен `LibVlcProvider`). Системный путь (`ShellExecuteEx`) убран.
- **Удаление — на STA-потоке, через IFileOperation.** `FileDeletionService` использует
  COM-интерфейс `IFileOperation` (Vista+ API, заменивший устаревший `SHFileOperation`),
  с флагом **`FOFX_NOCOPYHOOKS`** — он пропускает buggy shell extensions (TortoiseGit,
  антивирусные хуки и пр.), которые вызывали зависание `SHFileOperation`.
  Каждый вызов выполняется в **новом STA-потоке**; `SemaphoreSlim` ограничивает до 1
  одновременной операции, чтобы Explorer не захлёбывался.
  **Критично:** при зависании `await` вечно ждал бы результат из STA-потока, и
  `finally { _sem.Release(); }` никогда не отработал бы — семафор остался бы захвачен
  навсегда. Поэтому используется **`await Task.WhenAny(tcs.Task, Task.Delay(10с))`**;
  таймаут гарантирует, что семафор всегда освобождается. Старый поток (`IsBackground`)
  продолжает висеть, но приложение не умирает. Не убирай `Task.WhenAny`.
  Не возвращай на обычный `Task.Run` — `IFileOperation` требует STA.
  То же требование STA — у восстановления (ниже).
- **Отмена удаления.** `RecycleBinRestore` (COM `Shell.Application`, namespace Корзины = 10) находит
  элемент по `System.Recycle.DeletedFrom` + имя и вызывает глагол восстановления. Имя глагола
  **локализовано** (рус. «Восстановить», англ. «Restore») — матчится по набору имён, иначе берётся
  первый глагол. COM работает только в **STA** → выполняется на отдельном STA-потоке.
  `MainViewModel` хранит **стек** удалённых в Корзину файлов (`List<DeletedItem>`); каждое
  восстановление забирает верхний элемент синхронно до `await` и вставляет его обратно через
  `INavigationService.InsertAt` по сохранённому индексу. Повторные нажатия кнопки/клавиши
  восстанавливают файлы в порядке, обратном удалению, пока стек не опустеет. Только для удаления
  **в Корзину** (не «навсегда»); безвозвратное удаление, смена папки или смена сортировки сбрасывают
  стек.
- **Видео: аудиодорожки/субтитры/кадр** — через `MediaPlayer` LibVLC (`AudioTrackDescription`/`SetAudioTrack`,
  `SpuDescription`/`SetSpu`, `AddSlave` для внешних субтитров, `TakeSnapshot`). Списки дорожек доступны
  только **после старта воспроизведения** — меню строится по клику (live-запрос), как у кнопки скорости.
- **Контекстное меню (правый клик)** строится в code-behind вью (динамически — из-за live-списков
  дорожек): видео — `VideoViewerView.OnContextMenuOpening`, фото — `ImageViewerView.OnContextMenuOpening`.
  VM-специфичные пункты берут команды локального VM; навигация и действия с файлом — из `MainViewModel`,
  который вью достаёт через `Window.GetWindow(this).DataContext`. Общие пункты — в `MediaContextMenu`
  (статический помощник: `Item`/`Check`/`AddNavigation`/`AddFileActions`, с иконками `ui:SymbolIcon`).
- **Повторный клик по кнопкам аудио / субтитры / скорость** закрывает уже открытое `ContextMenu`.
  В `VideoViewerView` хранятся ссылки на текущие меню (`_audioMenu`, `_subtitleMenu`, `_speedMenu`);
  если `IsOpen == true` — выставляем `IsOpen = false` и выходим, иначе создаём новое меню.

### 5.11. Горячие клавиши и перехват фокуса нативным окном видео (airspace)

Нативное окно вывода видео LibVLC (airspace) при старте воспроизведения **перехватывает
клавиатурный фокус** у WPF-окна. Пока фокус не у WPF, обычный `PreviewKeyDown` НЕ срабатывает —
симптом: Delete / громкость (↑↓) / перемотка (←→) «не работают, пока не кликнешь по видео».

Поэтому горячие клавиши в `MainWindow` ловятся **двумя путями** (оба зовут единый `TryHandleHotkey`):

- `PreviewKeyDown` — когда фокус у элемента WPF;
- `ComponentDispatcher.ThreadPreprocessMessage` — перехват `WM_KEYDOWN` на уровне потока, работает
  даже когда фокус у нативного окна VLC. Если клавиша обработана — ставим `handled = true`
  (тогда сообщение не доходит до `DispatchMessage`, двойной обработки с `PreviewKeyDown` нет).

Нюансы, которые легко сломать:

- **Модальные диалоги.** `ThreadPreprocessMessage` — потоковое событие и срабатывает в т.ч. при
  открытом `ShowDialog` (настройки/свойства). Флаг `_suspendHotkeys` (ставится вокруг `ShowDialog`
  в `OpenSettings`/`OpenProperties`) глушит хоткеи, иначе Delete/F и т.п. сработают поверх диалога.
  Win32-диалоги (открытие файла) крутят свой цикл сообщений — их `ThreadPreprocessMessage` не видит.
- **Фокус в ComboBox/Slider/TextBox** — навигационные клавиши (стрелки/Space/±/скобки) отдаём
  контролу (проверка `inControl && isNavKey` в `TryHandleHotkey`).
- **Страховка.** `VideoViewerView` дополнительно возвращает фокус окну (`FocusHostWindow`) при
  старте воспроизведения (`IsPlaying → true`) и по клику на видео.
- **Восстановление удаления — Ctrl+Z или Page Up.** Обрабатывается в `TryHandleHotkey`
  (`Key.Z` + `ModifierKeys.Control`, либо `Key.PageUp` без модификатора) и зовёт
  `RestoreLastDeleteCommand`, работая даже когда фокус у нативного окна VLC. `Page Up` — жёстко
  зашитый дублёр Ctrl+Z, действует в любом контексте (фото/видео/пустой экран). `Page Up` входит
  в `AllConfigurableKeys` и может быть назначен на Exit/ToggleChrome/FullScreen — в этом случае
  настраиваемая функция приоритетнее (её ветка в `TryHandleHotkey` расположена выше switch).
- **Настраиваемая клавиша закрытия программы.** Добавлена настройка `ExitKey` (`AppSettings`),
  по умолчанию `End`. Обрабатывается в `MainWindow.TryHandleHotkey` (до `Escape`), парсится через
  `Enum.TryParse<Key>`. Не назначай навигационные клавиши (стрелки, Space) — иначе сломается
  управление видео/фото.
- **Настраиваемая клавиша скрытия/показа элементов управления.** Добавлена настройка
  `ToggleChromeKey` (`AppSettings`), по умолчанию `PageDown`. Для фото переключает
  `MainViewModel.ChromeVisible` (панель + стрелки + курсор), для видео отправляет
  `ToggleChromeMessage` — `VideoViewerView` скрывает/показывает `ControlBar` и боковые стрелки.
  Не назначай навигационные клавиши.
- **Настраиваемая клавиша полноэкранного режима.** Добавлена настройка `FullScreenKey`
  (`AppSettings`), по умолчанию `F11`. Обрабатывается в `MainWindow.TryHandleHotkey`;
  клавиша `F` оставлена как жёстко зашитый запасной вариант. Настройка изменяется
  в окне настроек; выпадающие списки фильтруют уже занятые другими действиями клавиши.
  - **Стрелки ←/→ и видео в обычном режиме.** По умолчанию `Left`/`Right` переключают файлы,
      а перемотка видео работает только в полноэкранном режиме. Настройка `ArrowKeysSeekVideo`
      (`AppSettings`, тумблер в разделе «Видео») меняет поведение: при открытом видео стрелки
      всегда вызывают `StepBackward`/`StepForward` (с учётом `FrameByFrameSeek` и `SeekStepSeconds`),
      независимо от `IsFullScreen`; для фото поведение не меняется — стрелки листают галерею.
      Реализовано в `MainWindow.TryHandleHotkey`.

### 5.12. Автоскрытие «плавающих» элементов и старт развёрнутым окном

- **Окно стартует развёрнутым.** В `MainWindow.xaml` задано `WindowState="Maximized"` (а `Width/Height`
  остаются «нормальным» размером для восстановления). Поле `_prevState` в code-behind инициализировано
  `Maximized` — это размер, в который возвращается окно при выходе из полноэкранного режима, если до
  входа в него состояние не отследили.
- **Фото: панель и стрелки скрываются по таймеру бездействия.** Состоянием рулит
  `MainViewModel.ChromeVisible` (по умолчанию `true`). Нижняя панель фото (`ImageViewerView`) привязана
  к `ChromeVisible` через `RelativeSource` к окну; боковые стрелки окна — через `ShowWindowNavArrows`
  (включает `ChromeVisible`). Таймер (`_chromeHideTimer`, 3 с) и `PreviewMouseMove` живут в `MainWindow`:
  движение мыши показывает элементы и перезапускает отсчёт, тик — прячет (и курсор — только в
  полноэкранном). Работает **только для фото** (`CurrentContent is ImageViewerViewModel`); видео и
  пустой экран таймер не трогает. Смена контента/режима зовёт `ResetChrome`.
  - `ResetChrome(forceShow: false)` при смене фото (в т.ч. после удаления) **сохраняет** текущее
    значение `ChromeVisible`: если элементы были скрыты, они остаются скрытыми; курсор в полноэкранном
    тоже остаётся скрытым (или скрывается таймером). Только при явном `forceShow=true` (смена
    полноэкранного режима) элементы принудительно показываются.
  - Стиль `AutoHideNavArrow` (в `AppResources.xaml`) НЕ меняли: стрелки в покое `Opacity=0.5`,
    ярче при наведении. Показ/скрытие по таймеру — через `Visibility` (привязка к `ShowWindowNavArrows`),
    а не через прозрачность.
- **Полноэкранный режим — настоящий бордерлесс через Win32.**
  `WindowState.Maximized` оставляет зону Aero Snap, поэтому используется ручное позиционирование
  (`MonitorFromWindow` + `GetMonitorInfo` + `SetWindowPos` на `rcMonitor`). При входе в полноэкран:
  - `WindowChrome` полностью убирается (`SetWindowChrome(this, null)`).
  - Через `SetWindowLong` у HWND снимаются `WS_CAPTION | WS_THICKFRAME | WS_MAXIMIZEBOX | WS_MINIMIZEBOX`.
  - Устанавливается **подкласс окна** (`comctl32!SetWindowSubclass`), который перехватывает
    `WM_NCHITTEST` и возвращает `HTCLIENT` для любой точки. Это единственный надёжный способ
    отключить resize-границы, потому что `TitleBar` из WPF-UI вешает собственный `HwndSourceHook`
    для `WM_NCHITTEST`, и простое изменение `WindowChrome` / `ResizeMode` не останавливает его.
  - `DwmExtendFrameIntoClientArea` с нулевыми margins + `DWMWA_BORDER_COLOR = DWMWA_COLOR_NONE`
    - `DWMWA_WINDOW_CORNER_PREFERENCE = DWMWCP_DONOTROUND` — убирают белые полосы DWM в Windows 11.
  - `WindowStyle` и `ResizeMode` через WPF **не меняются** (иначе `FluentWindow` в
    `OnExtendsContentIntoTitleBarChanged` принудительно сбросит `WindowStyle` обратно
    в `SingleBorderWindow` и рамка вернётся).
  При выходе всё восстанавливается в обратном порядке.
- **Видео: боковые стрелки скрываются вместе с панелью.** Кнопки `PrevFileButton`/`NextFileButton` в
  `VideoViewerView` больше НЕ привязаны к `ShowFileNavigation` в XAML — их видимостью управляет
  code-behind (`UpdateSideNav`): показываются только когда панель видна (`_controlsShown`) И файлов >1.
  Прячутся по тому же `_hideTimer`, что и `ControlBar`.
- **Видео и фото используют общее состояние ChromeVisible.** `VideoViewerView` синхронизирует
  `_controlsShown` с `MainViewModel.ChromeVisible` при каждом показе/скрытии панели. При переключении
  между фото и видео `RestoreControls()` берёт текущее `ChromeVisible`, поэтому элементы управления
  остаются в том же состоянии (скрыты/видны), а не появляются заново. Скрытие по таймеру,
  клавиша `ToggleChromeKey`, пауза и движение мыши обновляют это состояние и для видео, и для фото.
- **Видео: курсор скрывается через `Overlay.Cursor` + `Window.Cursor`, а не `UserControl.Cursor`.**
  Из-за airspace-окна LibVLCSharp.WPF (отдельное нативное HWND за WPF-оверлеем) установка
  `Cursor = Cursors.None` на уровне `VideoViewerView` не гарантирует скрытие курсора над видео:
  `MainWindow` может держать `Cursor = Cursors.Arrow`, который «пробивается» сквозь airspace.
  Поэтому `ShowControls`/`HideControlsIfPlaying` выставляют курсор явно на `Overlay`
  (Grid с фоном `#01000000`, который перехватывает hit-test поверх видео) и синхронно
  на `Window.GetWindow(this).Cursor`, чтобы весь HWND был согласован.
- **Инфо-плашка (имя, размер, порядок файла) в полноэкранном режиме скрывается вместе с chrome.**
  Для фото — зависит от `MainViewModel.ShowFullscreenInfo` (`IsFullScreen && ChromeVisible && StatusText не пуст`);
  для видео — `VideoViewerView.UpdateInfo` проверяет `_mainVm.IsFullScreen && _controlsShown`.
  Таким образом плашка показывается при движении мыши/взаимодействии и прячется по таймеру
  одновременно с панелью управления.
  **Важно:** внутри `VideoViewerView` нельзя биндиться через `RelativeSource AncestorType=Window`
  к свойствам `MainWindow`, потому что `LibVLCSharp.WPF` рендерит `VideoView.Content`
  в отдельном `ForegroundWindow` — `AncestorType=Window` найдёт уже это окно, а не `MainWindow`.
  Поэтому текст и видимость инфо-плашки видео задаются из code-behind через `_mainVm`.
  Для его получения используется `Window.GetWindow(this)?.DataContext`; если airspace-окно
  LibVLCSharp.WPF (ForegroundWindow) мешает, есть fallback на `Application.Current.MainWindow`
  — в этом приложении оно всегда главное окно с `DataContext = MainViewModel`.
- **Полноэкранный режим и airspace-окно видео.**
  После `ApplyFullScreen` (где размер/стиль окна меняются через Win32 API) вызывается
  `Dispatcher.BeginInvoke(UpdateLayout, DispatcherPriority.Render)`. Это нужно, чтобы
  `ForegroundWindow` LibVLCSharp.WPF получил событие `LayoutUpdated` и пересчитал позицию
  overlay-окна — иначе панель/инфо-плашка иногда оказываются смещены или не видны.
- **⚠️ Клик по кнопке «Полный экран» при живом видео вешал весь ПК (Event 41, без TDR).**
  WPF-кнопка снимает захват мыши **после** `OnClick` — команда исполняется, пока кнопка ещё
  держит захват. Синхронный рестайл/ресайз окна (`FullScreenHelper.Enter`) при захваченной мыши
  на фоне живого видео (нативный D3D11-HWND LibVLC внутри HwndHost) давал жёсткий фриз всей
  системы на AMD Radeon (перезагрузка). Горячая клавиша работала, т.к. при ней захвата нет.
  **Фикс:** `MainViewModel.ToggleFullScreen`/`ExitFullScreen` идут через
  `DeferFullScreenTransition` — `Mouse.Capture(null)` + `Dispatcher.BeginInvoke(…, Input)`,
  т.е. переход выполняется после завершения цикла ввода (клик завершён, захват снят), как у
  хоткея. Плюс подкласс `FullScreenSubclassProc` возвращает `HTCLIENT` только для точек ВНУТРИ
  окна (безусловный `HTCLIENT` для любой точки при захваченной мыши раздувал
  WM_NCHITTEST-шторм). Не делай переход в fullscreen синхронно внутри Click-обработчика и
  не возвращай `HTCLIENT` безусловно.
- **Мини-таймлайн видео при скрытой панели.** В `VideoViewerView` добавлен тонкий `ProgressBar`
  (`MiniTimeline`), который виден, когда основная панель управления скрыта. Видимость управляется
  из code-behind (`UpdateChromeVisibility`) вместе с `ControlBar`, чтобы не смешивать chrome-state
  и настройки. Показывается только если настройка `ShowMiniTimeline` включена, длительность видео
  известна и **строго меньше** `MiniTimelineThresholdMinutes` (по умолчанию 20 мин). На паузе панель
  управления всегда видна, поэтому мини-полоска не появляется. Элемент имеет `IsHitTestVisible=False`,
  чтобы клики по видео (пауза/полный экран) продолжали работать. Порог настраивается в окне настроек
  и применяется вживую.

### 5.13. Нюансы Фазы 1 (P1) — безопасность, архитектура, DI

- **Атомарное сохранение поворота.** ...
- `ImageDecodingService` **не глотает `OutOfMemoryException`** — ...
- **Отмена предыдущего открытия папки.** ...
- **Shutdown через `CancellationTokenSource`.** ...
- **Зомби-STA-потоки удаления.** ...
- **`MediaItem` не содержит UI-логики.** ...

### 5.14. Нюансы Фазы 2 (P2) — производительность, UX, валидация

- **Magick → BMP вместо PNG.** `ImageDecodingService.LoadWithMagick` пишет `MagickImage`
  в `MemoryStream` с `MagickFormat.Bmp` (вместо `Png`). BMP не сжимает → меньше latency,
  быстрее открытие HEIC/WebP большого размера.
- **`ZoomBorder` debounce + touch.** `LayoutUpdated` теперь throttled через `DispatcherTimer`
  (40 мс), а не вызывает `ApplyMode` на каждый чих визуального дерева. Добавлена обработка
  манипуляций (pinch/pan) для touch-экранов.
- **`ToastView` очередь.** Уведомления выстраиваются в очередь (max 3) и показываются
  последовательно. При спаме (например, массовое удаление) пользователь не теряет сообщения.
- **`SavePosition` оптимизирован.** `VideoViewerViewModel` больше не пишет JSON каждую секунду
  через `DispatcherTimer`. Позиция сохраняется при событиях: `Paused`, `Stopped`, `EndReached`,
  `SeekTo`, `Dispose`, а также явно при выходе (`App.OnExit` вызывает `Flush()` у
  `IPlaybackPositionStore`). `PlaybackPositionStore` и так debounce 1.5 с перед записью на диск.
- **`ImageCache` capacity = 24.** LRU-кэш полноразмерных изображений увеличен с 7 до 24 —
  быстрая прокрутка длинных серий фото не вызывает повторного декодирования на современных ПК.
- **Airspace alpha ≥ 2.** Фон оверлея `VideoViewerView` изменён с `#01000000` на `#02000000`
  — рекомендация LibVLCSharp.WPF для корректного hit-test (особенно в High Contrast).
- **Валидация `AppSettings`.** Свойства `SeekStepSeconds`, `SlideshowIntervalSeconds`,
  `DefaultPlaybackRate` помечены `[Range(...)]`. При загрузке `settings.json`
  `SettingsService.ValidateAndFix` сравнивает значения с атрибутами и заменяет невалидные
  на дефолтные (например, `"SeekStepSeconds": -5` → `5`).
- **Батчевание миниатюр в `ThumbnailStripViewModel`.** Вместо `InvokeAsync` на каждый
  готовый thumbnail — `ConcurrentQueue` + `DispatcherTimer` с интервалом 50 мс.
  Поток декодирования складывает результаты в очередь, UI-поток сбрасывает пачкой.
  Это предотвращает фризы при открытии папок с тысячами файлов.
- **`Dispatcher.Yield(DispatcherPriority.Render)` в ленте.** При добавлении порций записей
  в `ObservableCollection` (метод `SetItemsAsync`) `await Task.Yield()` заменён на
  `await Dispatcher.Yield(DispatcherPriority.Render)` — гарантирует, что WPF отрисует кадр
  между пачками и окно остаётся отзывчивым.
- **Ограничение памяти `ImageCache`.** Помимо лимита по количеству (`Capacity = 24`), кэш
  отслеживает приблизительный размер загруженных `BitmapSource`:
  `PixelWidth × PixelHeight × BitsPerPixel / 8`. При превышении 800 МБ самые старые
  элементы вытесняются по LRU, независимо от Capacity — процесс не раздувается >1 ГБ
  при листании тяжёлых RAW/HEIC.
- **Валидация горячих клавиш.** Из списка `ExitKeys` исключены навигационные и критичные
  клавиши (`Delete`, `Left`, `Right`, `Up`, `Down`, `Space`, `Escape` и т.д.). При загрузке
  `settings.json` некорректные значения `ExitKey`/`ToggleChromeKey` подменяются на безопасные
  дефолты (`End` / `PageDown`), чтобы случайно не сломать навигацию или закрытие.
- **Остановка GIF при смене файла.** В `ImageViewerView.DetachVm` вызывается
  `AnimationBehavior.SetSourceUri(AnimatedImage, null)` — декодер XamlAnimatedGif
  освобождает потоки и GDI-ресурсы до того, как View переиспользуется для следующего файла.
- **Ротация `AppLog`.** При старте, если `app.log` превышает 10 МБ, он переименовывается
  в `app.log.old` (одна резервная копия). Старый `.old` при этом удаляется — лог не растёт
  бесконечно.
- **`SetVideoUnavailable` сбрасывает все поля.** В `FilePropertiesViewModel` условие
  `&& _durationRow.Value == "…"` убрано: при ошибке или таймауте `Media.Parse` все поля
  видео (разрешение, длительность, FPS) сбрасываются на `"—"`, а не показывают
  устаревшие/частичные значения.

---

### 5.15. Нюансы Фазы 3 (P3) — рефакторинг и техдолг

- **`MainViewModel` разделён на partial-классы.** Код VM разбит на 5 файлов:
  `MainViewModel.cs` (общее, DI, конструктор, `UpdateCurrentContent`, `RefreshCommandStates`),
  `MainViewModel.Gallery.cs` (открытие, сортировка, Drag-and-Drop),
  `MainViewModel.Navigation.cs` (Next/Previous),
  `MainViewModel.Presentation.cs` (FullScreen, Slideshow),
  `MainViewModel.Deletion.cs` (Delete, Restore, Undo-state),
  `MainViewModel.FileActions.cs` (ShowInExplorer, CopyPath, Properties).
  Source generators CommunityToolkit.Mvvm поддерживают `partial class` — `[ObservableProperty]`
  и `[RelayCommand]` работают корректно во всех частях. Компилятор связывает partial methods
  (`OnSelectedSortFieldChanged` и т.п.) независимо от того, в каком файле объявлено свойство,
  а в каком — реализация.
- **`IFileDeletionService` возвращает `DeleteResult`.** Вместо `Task<bool>` используется
  `Task<DeleteResult>` где `DeleteResult` — `record` с `bool Success` и `string? ErrorMessage`.
  Это позволяет `MainViewModel` самому решать, показывать ли тост, и с каким текстом.
  `FileDeletionService` не отвечает за UI-уведомления.
- **`FilePropertiesWindow` через DI.** Окно зарегистрировано как `transient` в контейнере,
  получает `LibVlcProvider` в конструкторе, а `MediaItem` — через метод `Initialize` после
  резолва из DI. `MainWindow.OpenProperties` больше не использует `new FilePropertiesWindow(...)`.
- **`AutomationProperties.Name` на кнопках видео.** Все интерактивные кнопки `VideoViewerView`
  (Play/Pause, Mute, Audio, Subtitles, Snapshot, Speed, FullScreen, боковые стрелки Prev/Next)
  снабжены `AutomationProperties.Name` для корректной работы экранного диктора и accessibility.
- **Утечка `EmptyStateViewModel` (P0.1).** В `MainViewModel.UpdateCurrentContent` старый контент
  освобождался только при явном приведении к `ImageViewerViewModel`/`VideoViewerViewModel`.
  `EmptyStateViewModel` тоже подписан на события (`RecentFilesService.Changed`) и содержит
  `DispatcherTimer`. При смене `CurrentContent` проверяем `old is IDisposable`, а не
  конкретные типы — тогда любой VM со своими подписками корректно отписывается.
- **Залипание панорамы в `ZoomBorder` (P0.2).** `OnLostMouseCapture` обязан сбрасывать
  `_dragging = false` и `Cursor = Cursors.Arrow`. Если во время перетаскивания курсор уходит
  за край окна и отпускается там, `OnMouseLeftButtonUp` не сработает, а `OnLostMouseCapture` —
  да. Без сброса флага изображение «прилипает» к курсору при возвращении мыши в окно.
- **Батчевание миниатюр (P2.1).** `Dispatcher.InvokeAsync` на каждый готовый thumbnail при
  открытии папки из 5000 файлов фризит UI: потоки декодирования слишком часто переключаются
  на UI-поток. Заменено на `ConcurrentQueue` + `DispatcherTimer` с интервалом 50 мс:
  поток складывает результаты, UI сбрасывает пачкой. Также `await Task.Yield()` заменён
  на `await Dispatcher.Yield(DispatcherPriority.Render)` — гарантирует отрисовку кадра
  между порциями.
- **Cleanup transient VM при shutdown (P1.1).** При закрытии окна на видео `MainViewModel.Dispose()`
  должен вызвать `(CurrentContent as IDisposable)?.Dispose()` **до** `_host.Dispose()`.
  Иначе LibVLC выгружается раньше, чем освобождаются `MediaPlayer`/`Media`, что приводит к
  AV или зависанию. `App.OnExit` осуществляет это явно перед выгрузкой хоста.
- **`SetWindowLongPtr` для x64 (P3.2).** На x64 `SetWindowLong`/`GetWindowLong` возвращают 32-битное
  значение (`int`), что некорректно для `GWL_STYLE` на 64-битной Windows. Используем обёртки
  `SetWindowLongPtr`/`GetWindowLongPtr` с entry-point-переключением (`IntPtr.Size`) — безопасно
  для x64-only проекта.
- **Рефакторинг fullscreen в `Infrastructure/FullScreenHelper` (P3.3).** Вся Win32-логика
  полноэкранного режима (`MonitorFromWindow`, `DwmSetWindowAttribute`, подкласс окна и т.д.)
  изолирована в статическом классе `FullScreenHelper`. `MainWindow` вызывает
  `FullScreenHelper.Enter(this)` / `Exit(this, state)` — code-behind окна уменьшился на ~150 строк.
- **Фабрика `MainWindow` в DI (P3.4).** `MainWindow` зарегистрирован как `transient` с фабрикой
  `services.AddTransient(sp => new MainWindow(...))` вместо `AddSingleton<MainWindow>()`.
  Это позволяет контейнеру создавать новые окна при необходимости и держать конструктор
  публичным без неявного вызова ActivatorUtilities.
- **Убран Service Locator из `ToastView` (P3.1).** `ToastView` больше не обращается к
  `Application.Current as App`. Вместо этого он получает `INotificationService` через
  публичное свойство `MainViewModel.NotificationService`, которое доступно через
  `Window.GetWindow(this)?.DataContext`.

### 5.16. Дублирование экрана (Display Topology / Clone Mode)

Функция переключения Windows в системный clone-режим («Дублировать эти экраны») через CCD API:

- **API:** `QueryDisplayConfig` / `SetDisplayConfig` (P/Invoke в `Infrastructure/DisplayConfigApi.cs`).
- **Сервис:** `DisplayTopologyService` (singleton), реализует `IDisplayTopologyService`.
- **UI:** кнопка в нижней панели фото (`ImageViewerView`) и в оверлее видео (`VideoViewerView`), горячая клавиша `F12`.

**Критичные нюансы:**

- **Это системный clone mode, не «окно приложения».** `SDC_TOPOLOGY_CLONE` клонирует **весь desktop** (панель задач, окна, всё). Это то же самое, что Win+P → «Дублировать». Экраны **мигают/темнеют на 1–2 секунды** при переключении — это неизбежное поведение драйвера.
- **HRESULT проверяется через `hr != 0`, а НЕ `hr < 0`.** `SetDisplayConfig`/`QueryDisplayConfig` возвращают обычные Win32 error codes: `0` = успех, **положительное число** = ошибка (`ERROR_BAD_CONFIGURATION = 1610`, `ERROR_INSUFFICIENT_BUFFER = 122`, `ERROR_INVALID_PARAMETER = 87` и т.д.). Проверка `hr < 0` никогда не ловит ошибки.
- **`QueryDisplayConfig` P/Invoke — массивы должны быть `[Out]`.** Без `[Out]` marshaller может не записывать данные в managed массивы, и `QueryDisplayConfig` возвращает пустые структуры (все поля = 0), хотя HRESULT = 0. Исправлено: `[In, Out]` → `[Out]` для `pathArray` и `modeInfoArray`.
- **`QDC_DATABASE_CURRENT` + `IntPtr.Zero` = `ERROR_INVALID_PARAMETER` (87).** `QueryDisplayConfig` с флагом `QDC_DATABASE_CURRENT` **требует**, чтобы последний параметр (`currentTopologyId`) был не-NULL (`out DISPLAYCONFIG_TOPOLOGY_ID`). Поэтому `SaveCurrentConfig` использует `QDC_ONLY_ACTIVE_PATHS` (где `NULL` допустим), а не `QDC_DATABASE_CURRENT`.
- **Fallback `RestoreExtend` — `SDC_TOPOLOGY_EXTEND`, а не `SDC_USE_DATABASE_CURRENT`.** `SDC_USE_DATABASE_CURRENT` возвращает последнюю сохранённую конфигурацию вообще; если в persistence database последняя запись — clone, Windows просто вернёт clone. `SDC_TOPOLOGY_EXTEND` явно запрашивает extend. Добавлены модификаторы `SDC_ALLOW_CHANGES | SDC_PATH_PERSIST_IF_REQUIRED | SDC_VIRTUAL_MODE_AWARE`, чтобы Windows могла подобрать рабочий режим, даже если в базе нет готовой extend-записи.
- **`SDC_VIRTUAL_MODE_AWARE` добавлено ко всем вызовам.** Windows 10+ использует virtual modes для clone; без этого флага `SetDisplayConfig` может отвергнуть валидную конфигурацию.
- **Восстановление при shutdown.** `MainWindow.OnClosing` (override, не событие) и `App.OnExit` безусловно вызывают `RestoreExtend()`. Дублирование безопасно — `RestoreExtend` ранний return, если `!IsCloned`. При **нормальном** закрытии окна (`X`, `Alt+F4`) `OnClosing` гарантированно отработает. Если процесс **убивается** через Task Manager / `Stop-Process` — `OnClosing`/`OnExit` не вызываются; это ограничение Windows.
- **Отключение монитора.** `MainWindow` ловит `WM_DISPLAYCHANGE` через `HwndSource.AddHook` и передаёт в `DisplayTopologyService.OnDisplaySettingsChanged()`. Сброс `IsCloned` автоматически **не производится** при `!CanClone` (в clone-режиме `GetSystemMetrics(SM_CMONITORS)` часто возвращает 1). Если монитор реально отключился — Windows сама переключит topology и пришлёт `WM_DISPLAYCHANGE`.
- **Обновление CanExecute команды.** `MainViewModel` при событии `TopologyChanged` вызывает `RefreshCommandStates()`, чтобы `ToggleCloneDisplayCommand` корректно обновила состояние `CanExecute`.
- **Видео: кнопка clone не через `RelativeSource AncestorType=Window`.** Из-за airspace LibVLCSharp.WPF `AncestorType=Window` в XAML найдёт `ForegroundWindow`, а не `MainWindow`. Поэтому в `VideoViewerView` видимостью и кликом кнопки управляет code-behind через `_mainVm` (кэшированный в `OnLoaded`). Все клики логируются в `app.log`. Для фото такой проблемы нет — биндинг через `RelativeSource AncestorType=Window` работает.
- **Диагностика.** Все операции clone/extend логируются с префиксом `[Clone]` в `app.log`. Если функционал не работает — первым делом смотреть `%LOCALAPPDATA%\Prosmotr\app.log`.

### 5.17. Оптимизации холодного старта (SplashScreen, Composite R2R, ленивые DataTemplate'ы)

При первом запуске после перезагрузки Windows (холодный старт) приложение может стартовать
медленнее (~10 с), чем при последующих запусках (~1 с). Основные причины: JIT-компиляция,
загрузка нативных DLL (LibVLC, Magick.NET), парсинг тяжёлого XAML, I/O реестра.

**Внесённые оптимизации:**

1. **SplashScreen (`System.Windows.SplashScreen`)** — `App.xaml.cs`: показывает `app.ico`
   мгновенно, ещё до инициализации DI и LibVLC. Закрывается автоматически при активации
   `MainWindow` (или явно через `splash.Close(...)`).
2. **Composite ReadyToRun** — в `Prosmotr.csproj` включён `<PublishReadyToRunComposite>true`.
   При публикации (`dotnet publish -c Release -o app`) создаётся единый нативный образ,
   уменьшающий количество page faults и остаточной JIT при холодном старте.
3. **Ленивые DataTemplate'ы** — тяжёлые шаблоны `ImageViewerView` и `VideoViewerView`
   (последний тянет `LibVLCSharp.WPF`) убраны из `AppResources.xaml` и перенесены в
   `<Window.Resources>` `MainWindow.xaml`. `EmptyStateView` (лёгкий) оставлен в `App.xaml`,
   т.к. стартовый экран нужен сразу. Таким образом парсинг `VideoViewerView.xaml` откладывается
   до момента, когда основное окно уже на экране.
4. **Фоновая shell-интеграция** — `TryIntegrateShell()` теперь выполняется через
   `Task.Run(TryIntegrateShell)`, чтобы синхронные операции с реестром не блокировали UI-поток.
5. **Кэширование `plugins.dat` в `%LOCALAPPDATA%\Prosmotr`.** Генерация кэша плагинов LibVLC
   (`libvlc\win-x64\plugins\plugins.dat`) занимает 5–10 с на холодном старте. `LibVlcProvider.Warmup`
   теперь: (а) при отсутствии кэша в `app\` копирует его из `%LOCALAPPDATA%\Prosmotr\plugins.dat`,
   если он там есть; (б) после генерации сохраняет свежий кэш в `%LOCALAPPDATA%`, чтобы
   последующие запуски после очистки/перепубликации `app\` не ждали повторного сканирования.
   Скрипт `publish.ps1` также делает локальную резервную копию `plugins.dat` на время очистки.

**Что НЕ поможет (не делать):**

- `PublishSingleFile` — **запрещён**, ломает загрузку нативных плагинов LibVLC.
- Self-contained publish — увеличивает размер и не ускоряет старт.
- Trimming (ILLink) — несовместим с WPF-рефлексией и XAML.

---

### 5.18. Защита от краевых случаев (Edge-case hardening)

Итерация исправлений, направленных на устойчивость при «пограничных» сценариях:

- **`UriFormatException` в путях с `#`/`%`.** `new Uri(path, UriKind.Absolute)` выбрасывает
  `UriFormatException` для путей, содержащих `#` или неэкранированный `%`. Это затронуло
  `VideoPlaybackService.AddSubtitleFile`, `ImageViewerViewModel.AnimatedSource` (XamlAnimatedGif)
  и `FilePropertiesViewModel.LoadVideoInfoAsync`. Везде добавлен fallback через `UriBuilder`
  (схема `file`, путь as-is) — тот же паттерн, что уже использовался в `VideoPlaybackService.Load`.
- **Утечки `CancellationTokenSource`.** `MainViewModel._openCts`, `ThumbnailStripViewModel._cts`,
  `ImageViewerViewModel._cts` создавались при каждой новой операции, но старые экземпляры
  не освобождались. Добавлен `_cts?.Dispose()` перед созданием нового.
- **`ZoomBorder.Child` setter терял `SizeChanged`.** При повторном назначении того же `Image`
  (переиспользование View) подписка на `SizeChanged` сбрасывалась и не восстанавливалась,
  потому что `ReferenceEquals` блокировал весь блок. Исправлено: отписка от старого child,
  затем подписка на новый всегда, независимо от `ReferenceEquals`.
- **Атомарная запись `PlaybackPositionStore.Flush`.** `positions.json` писался напрямую;
  при аварийном завершении процесса файл мог обрезаться. Теперь: `tmp` → `File.Replace`
  (или `File.Move`), как у `SettingsService`.
- **Shutdown race в `NotificationService.Raise` и `VideoViewerViewModel.OnUi`.**
  `Dispatcher.BeginInvoke` может выбросить `InvalidOperationException`, если `Dispatcher`
  уже shutting down. Добавлен `try/catch` — события при закрытии приложения игнорируются.
- **`RecycleBinRestore.RunStaAsync` — `RunContinuationsAsynchronously`.** Без этого флага
  продолжение `TaskCompletionSource` могло выполняться на STA-потоке и вызывать deadlock.
- **`MainViewModel.OnListChanged` — защита `async void`.** Необработанное исключение в
  `async void` убивало процесс через `DispatcherUnhandledException`. Тело обёрнуто в
  `try/catch` с маршрутизацией в `AppLog`.
- **`ResolveOrderingAsync` — `ExplorerSortReader` fault.** `Task.Run(() =>
  ExplorerSortReader.TryGetOrderedPaths(...))` мог fault, если COM Explorer выбросил.
  Необработанный faulted task приводил к краху `OpenPathAsync`. Обёрнуто в `try/catch`,
  возвращаем `null` при ошибке.

---

### 5.19. Аудит P4 — гонки, утечки и устойчивость

Итерация по результатам полного аудита (сервисы, VM, infrastructure, App/Views):

- **`ImageCache`: внешний `ct` больше НЕ привязывается к кэшируемой задаче.** Кэш — singleton,
  и его `Task` общий для всех вызывающих. Раньше `CreateLinkedTokenSource(ct, cts.Token)`
  привязывал токен **первого** вызывающего к задаче в кэше: когда навигация уходила дальше и
  его `ct` отменялся, декодирование отменялось и для всех последующих (мигание/повторное
  декодирование). Теперь декодируем под токеном только из `cts` (живёт с записью кэша), а отмену
  вызывающего применяем к ожиданию через `task.WaitAsync(ct)` (хелпер `ForCaller`). При отмене
  вызывающий получает `OperationCanceledException`, сама задача в кэше не страдает.
  Парно: `ImageViewerViewModel.LoadAsync` ловит `OperationCanceledException` **отдельным** catch
  (отмена ≠ ошибка), иначе на отменённом кадре мигал бы `HasError`.
- **`AppLog.Write` потокобезопасен.** Лог пишут UI, STA-потоки удаления/восстановления и фоновые
  декодеры. `File.AppendAllText` без синхронизации бросал `IOException` под нагрузкой (записи
  терялись) и конфликтовал с ротацией. Добавлен `lock (_gate)` вокруг всей операции.
- **`RestoreLastDelete` защищён от повторного входа и работает со стеком.** Кнопка «Отменить» есть
  и на тосте, и на панели; `await RestoreAsync` длительный. `MainViewModel` хранит стек удалённых
  файлов, верхний элемент забирается и удаляется из стека **синхронно до** `await`, а флаг
  `_isRestoring` блокирует параллельные вызовы. Повторное нажатие восстанавливает следующий файл
  из стека, а не тот же самый.
- **Single-instance: `ReleaseMutex` только на владеющем экземпляре.** Второй экземпляр создаёт
  `Mutex(true, …, out isFirstInstance)`, но ownership не получает. `OnExit` вызывал
  `ReleaseMutex()` всегда → `ApplicationException` (проглатывался). Добавлен флаг `_ownsMutex`.
- **Пути со спецсимволами (`#`, `%`).** Прежний fallback `new UriBuilder { Path = path }` ломался
  на `#` (трактуется как фрагмент) — ровно там, где должен был помочь. Теперь:
  - `VideoPlaybackService.Load` — через `new Media(libVlc, path, FromType.FromPath)` (без URI вообще);
  - `VideoPlaybackService.AddSubtitleFile` (`AddSlave` требует MRL) и `ImageViewerViewModel.AnimatedSource`
    (XamlAnimatedGif требует `Uri`) — экранирование `%`→`%25`, затем `#`→`%23`, потом `new Uri`
    (хелпер `VideoPlaybackService.ToFileUri`). Порядок замен важен (сначала `%`).
- **Гонка `SwitchTo`/`SavePosition` (видео→видео).** Асинхронные события старого плеера
  (`Stopped`/`TimeChanged`) могли записать позицию под путём уже **нового** файла. Введён флаг
  `_switching`: ставится в `SwitchTo` (после сохранения позиции старого файла), снимается в
  `OnPlaying` нового видео; `SavePosition` подавлен, пока он взведён.
- **`MainWindow`: `HwndSource` hook снимается при закрытии.** `OnLoaded` добавлял `WndProcHook`
  через `AddHook`, но `RemoveHook` не вызывался нигде (асимметрия с `ThreadPreprocessMessage`).
  Источник кэшируется в `_hwndSource`, снимается в `Closed`; `OnLoaded` защищён от повторного хука.
- **`OpenPathAsync`: проверка отмены перед применением результата.** За время `await` (до 3 с в
  `ResolveOrderingAsync`) пользователь мог открыть другой путь. Перед `SetItems` добавлен
  `if (ct.IsCancellationRequested) return;` — иначе побеждал результат, финишировавший последним,
  а не запрошенный последним.
- **`RecentFilesService`: атомарная подмена списка.** `Add`/`Clear` мутировали живой
  `Settings.RecentFiles`, пока дебаунс-таймер `SettingsService` сериализовал `Settings` на фоновом
  потоке → `InvalidOperationException` внутри `JsonSerializer` (настройки молча не сохранялись).
  Теперь собирается новый список и присваивается ссылке атомарно.
- **Атомарная запись `settings.json`/`positions.json` без TOCTOU.** Ветка
  `File.Exists ? Replace : Move` заменена на `File.Move(tmp, file, overwrite: true)`
  (MoveFileEx + `MOVEFILE_REPLACE_EXISTING`, атомарно на одном томе).
- **`ThumbnailStripViewModel` реализует `IDisposable`.** Владеет `CancellationTokenSource` и
  `DispatcherTimer` (создаётся через `new` в `MainViewModel`). `MainViewModel.Dispose` теперь
  вызывает `ThumbnailStrip.Dispose()` и `WeakReferenceMessenger.Default.UnregisterAll(this)`.
  Стартовый интервал слайдшоу в конструкторе клампится `Math.Clamp(…, 1, 60)` (битый settings.json).

### 5.20. Аудит P4 (вторая волна) — сортировка, удаление, декодирование

- **Сортировка устойчива к нетранзитивному `StrCmpLogicalW`.** Натуральная сортировка
  (`NaturalStringComparer` → WinAPI `StrCmpLogicalW`) на некоторых наборах имён нарушает
  транзитивность; `List<T>.Sort` (introsort) это детектит и бросает `InvalidOperationException`,
  обрушивая `ScanAsync`/`BuildFromFolderAsync` → открытие папки падало. Введён
  `MediaLibraryService.StableSort`: `try { list.Sort(cmp); } catch (InvalidOperationException)` →
  fallback на `OrderBy(..., Comparer.Create(cmp))` (LINQ не валидирует компаратор так жёстко).
  Используется во всех трёх местах сортировки (`Sort`, обе ветки `ApplyOrder`).
- **`FileDeletionService`: таймаут на `_sem.WaitAsync`.** Раньше — без таймаута; при
  патологическом залипании семафора UI-команда удаления зависла бы навсегда. Теперь
  `WaitAsync(TimeSpan.FromSeconds(30))`; при неуспехе — `DeleteResult(false, "…занята…")`.
  `_sem.Release()` вызывается только если семафор реально захвачен (ранний return до try).
- **`ShellService.OpenWith`: путь в кавычках.** `rundll32 shell32.dll,OpenAs_RunDLL <path>` без
  кавычек разбивал путь с пробелами на аргументы → диалог «Открыть с помощью» получал обрезанный
  путь. Путь заключён в `"…"`.
- **`FilePropertiesViewModel`: `Media(..., FromType.FromPath)`** вместо сломанного
  `UriBuilder { Path }` (тот же баг с `#`/`%`, что в `VideoPlaybackService`).
- **`ImageDecodingService` (Magick-миниатюры): порог уменьшения по максимальному измерению.**
  Условие `image.Width > box` пропускало высокие узкие WEBP/HEIC → полный декод для миниатюры.
  Теперь `image.Width > box || image.Height > box`.
- **`App.OnSecondInstance`: guard от гонки с shutdown.** Если named pipe сработал, когда хост
  уже выгружается, обращение к `Services` бросило бы `ObjectDisposedException`. Добавлена
  ранняя проверка `_appCts.IsCancellationRequested`.
- **`ZoomBorder` НЕ трогаем.** «Накопление» `TransformGroup` происходит только для новых
  экземпляров `Child`, а `Image` здесь переиспользуется (проверка `ReferenceEquals` сохраняет
  трансформ). Очистка `RenderTransform` при detach сломала бы зум при переключении фото.
- **`DisplayTopologyService.OnDisplaySettingsChanged`: сравнение с КЭШЕМ `_lastCanToggle`.**
  Раньше `wasCanToggle`/`wasCanClone` вычислялись «на лету» из текущего числа мониторов, которое
  к моменту `WM_DISPLAYCHANGE` уже изменилось → `было != стало` всегда false, событие
  `TopologyChanged` не поднималось, и кнопка дублирования не активировалась/деактивировалась при
  подключении/отключении второго монитора. Теперь предыдущее значение `CanToggle` хранится в поле
  `_lastCanToggle` (инициализируется в конструкторе, обновляется в `EnableClone`/`RestoreExtend`),
  событие поднимается только при реальном изменении.
- **`SupportedFormats.AllExtensions`: `Distinct(StringComparer.OrdinalIgnoreCase)`** — дефенсив от
  дубликатов расширений в разном регистре (наборы — case-insensitive, `Distinct` по умолчанию — нет).

### 5.21. Юнит-тесты (P4) — `tests/Prosmotr.Tests`

Появился первый тестовый проект (раньше тестов не было). **Что важно знать:**

- **Только чистая логика, без UI/нативов.** Тесты НЕ грузят LibVLC/Magick и НЕ создают WPF
  `Application` — поэтому быстрые и headless. Покрыты: `NavigationService` (индексы при
  remove/insert, циклическая навигация, `ReorderPreservingCurrent`), `MediaLibraryService.Sort`
  (натуральная сортировка, убывание, по размеру/дате + регрессионный тест устойчивости
  `StableSort` к нетранзитивному `StrCmpLogicalW` на 2000 именах), `SupportedFormats`
  (классификация, `AllExtensions` без дублей), `NaturalStringComparer`, `RecentFilesService`
  (дедуп, лимит 15, атомарная подмена ссылки списка, событие `Changed`).
- **TFM/платформа.** `net8.0-windows` + `PlatformTarget x64` — чтобы ссылаться на основную
  WPF-сборку (она `net8.0-windows`, x64). `UseWPF=true` в тестовом csproj нужен для совместимости
  ссылки, но WPF-типы в тестах не инстанцируются.
- **Фейки вместо моков.** Для `RecentFilesService` используется ручной `FakeSettings :
  ISettingsService` (in-memory) — без mock-фреймворков.
- **Seam для путей хранения.** `SettingsService` и `PlaybackPositionStore` принимают
  необязательный `string? directory` в конструкторе (по умолчанию — `%APPDATA%`/`%LOCALAPPDATA%`).
  Тесты передают временную папку (`TempDir`, удаляется по `Dispose`) и проверяют атомарную
  запись, перезагрузку, валидацию битого `settings.json`, round-trip позиций и регистр путей.
  **DI не меняется:** контейнер `Microsoft.Extensions.DependencyInjection` подставляет
  значение по умолчанию для optional-параметра — регистрация `AddSingleton` работает как прежде.
- **Что НЕ покрыто и проверяется вручную (см. §7):** UI, декодирование, COM/Win32 (удаление,
  clone-режим, fullscreen, ExplorerSortReader), airspace видео.
- **Запуск:** `dotnet test tests\Prosmotr.Tests\Prosmotr.Tests.csproj`. Если добавляешь логику в
  сервисы навигации/библиотеки/форматов — добавь тест в рамках того же изменения.

### 5.22. Оркестрированный аудит P5 (цикл 1) — многоагентная верификация

Итерация по результатам многоагентного аудита (10 finder-измерений + адверсариальная верификация
каждой находки). Подтверждённые и исправленные дефекты:

- **Лента миниатюр: переиспользование готовых миниатюр при rebuild.** `NavigationService`
  поднимает `ListChanged` на КАЖДУЮ мутацию (`RemoveAt`/`InsertAt`/`ReorderPreservingCurrent`),
  и `ThumbnailStripViewModel.SetItemsAsync` раньше `Items.Clear()` + пересоздавал все entry +
  декодировал ВСЕ миниатюры заново (дорого для WEBP/HEIC через Magick, мигание ленты). Теперь
  перед `Clear()` снимается снимок `prevThumbs` (path → ImageSource), и при rebuild готовые
  миниатюры переиспользуются; `LoadThumbnailsAsync` декодирует только `Thumbnail == null` и
  **пропускает приоритетный entry** в `Parallel.ForEachAsync` (раньше он декодировался дважды).
- **App: гонка warmup-continuation со shutdown.** `libvlcWarmup.ContinueWith → Dispatcher.Invoke →
  vm.InitializeAsync` не проверял отмену и не был в try/catch. При закрытии окна во время прогрева
  LibVLC (3-8 с) continuation дёргала VM/сервисы после `Dispose`. Добавлены проверка
  `_appCts.IsCancellationRequested` (снаружи и внутри Invoke) и `try/catch (InvalidOperationException)`
  — как в `OnSecondInstance`/pipe-сервере. Faulted-таск логируется.
- **App: named pipe читает ограниченный объём.** `StreamReader.ReadLineAsync` без лимита →
  локальный процесс мог слать гигабайты без `\n` (DoS по памяти). Заменено на чтение в буфер
  фикс. размера (64 КБ) с остановкой на первой строке.
- **SettingsService: гонка сериализации `ManualFolderSorts`.** Словарь мутировался по месту, пока
  debounce-таймер сериализовал `Settings` на пуле → `InvalidOperationException` (настройки молча
  не сохранялись). Теперь атомарная подмена словаря в `PersistAndApplySort` — как у `RecentFiles`.
- **MediaLibraryService: один обход метаданных вместо двух.** `Directory.EnumerateFiles` +
  `new FileInfo(path)` (повторный stat на каждый файл) заменены на `new DirectoryInfo(folder)
  .EnumerateFiles(...)` — `FileInfo` приходит с уже заполненными из записи каталога размером/датами
  (важно для сетевых дисков и больших папок).
- **MediaItem: производные имена кэшируются.** `FileName`/`Extension`/`DirectoryPath` вычислялись
  через `Path.*` при каждом обращении (горячий путь компаратора сортировки O(n log n)). `FullPath`
  неизменяем — вычисляем один раз в конструкторе.
- **Хоткеи: устранён рассинхрон списков.** `F`/`F12`/`M`/`Decimal` добавлены в `ConflictingKeys`
  и убраны из предлагаемых `ExitKeys` — пользователь больше не может назначить на Exit/Chrome
  клавишу, жёстко занятую (полный экран / клон / mute / удаление).
- **Настройка `AutoHideControls` ожила.** Раньше сохранялась и имела тоггл, но нигде не читалась.
  Теперь `MainWindow` (фото: `RestartChromeTimer`/`OnChromeHideTick`) и `VideoViewerView`
  (видео: `ShowControls` через `VideoViewerViewModel.AutoHideControls`) уважают её: при `false`
  таймер автоскрытия не запускается. Дефолт `true` — поведение по умолчанию не изменилось.
- **DRY: экранирование путей `#`/`%` вынесено в `Infrastructure/PathUri`.** `PathUri.ToUri`/`Escape`
  используются в `VideoPlaybackService.ToFileUri` и `ImageViewerViewModel.AnimatedSource`
  (раньше дублировался inline-код с порядком замен).

Тесты: добавлены `PathUriTests`, `MediaLibraryScanTests` (интеграционные на реальных файлах).
Всего юнит-тестов — 84.

### 5.23. Оркестрированный аудит P5 (цикл 2) — leaks/logic/interop/errorhandling

Второй цикл многоагентной верификации (измерения logic/errorhandling/interop/leaks/concurrency/
architecture). Подтверждено и исправлено 11 дефектов:

- **Двойная подписка `_mainVm` в `VideoViewerView` (утечка).** `OnDataContextChanged` и `OnLoaded`
  оба делали `+=` для одного экземпляра → один `OnMainVmPropertyChanged` оставался висеть на
  singleton `MainViewModel`, удерживая выгруженный View (с нативным HWND VLC). Введён идемпотентный
  `AttachMainVm(vm)` (сначала `-=`, потом `+=`, no-op если тот же VM); `DetachMainVm` = `AttachMainVm(null)`.
- **Удаление видео воссоздавало его resume-позицию.** `Delete` вызывал `_positions.Remove` ДО
  `RemoveAt`, а `RemoveAt`→`SwitchTo`/disposal старого плеера синхронно делал `SavePosition`
  удаляемого файла. `Remove` перенесён ПОСЛЕ `RemoveAt`.
- **Гонка `_thumbBatchTimer` между перекрывающимися `SetItemsAsync`.** Хвост устаревшей загрузки
  мог остановить/обнулить таймер новой ленты (миниатюры не выгружались). Таймер привязан к
  локальной ссылке; поле обнуляется только если `ReferenceEquals(_thumbBatchTimer, localTimer)`.
- **`OpenPathAsync`: `OperationCanceledException` → ложная ошибка.** OCE из отменённого скана
  попадала в общий `catch` (тост «Не удалось открыть» + ERROR в логе). Добавлен тихий
  `catch (OperationCanceledException)`.
- **`RecycleBinRestore`: утечка RCW COM-объектов.** Освобождался только корневой `shell`;
  `recycleBin`/`items`/каждый `item`/`verbs`/`verb` — нет. Добавлен `Release(...)` для всех
  промежуточных RCW на STA-потоке (по конвенции `ExplorerSortReader`/`FileDeletionService`).
- **`ExplorerSortReader` вызывался из MTA-пула.** Shell-COM (STA-only) маршалился через прокси —
  «сортировка как в Проводнике» могла молча отказывать. Введён `Infrastructure/StaTask.Run<T>` —
  выделенный STA-поток; используется и в `ResolveOrderingAsync`, и в `RecycleBinRestore`
  (старый приватный `RunStaAsync` удалён, DRY).
- **Отмена удаления после пересортировки.** Индексы удалённых файлов устаревали при смене
  сортировки → восстановление вставляло файл не на место. `PersistAndApplySort` теперь вызывает
  `ClearUndoState()`, сбрасывая весь стек отмены.
- **Слайд-шоу обрывало видео.** Таймер безусловно делал `MoveNext` каждые N сек. Теперь
  `OnSlideshowTick` пропускает тик, если `CurrentContent is VideoViewerViewModel { IsEnded: false }`.
- **`ShellService`: `Process.Start` не освобождался.** Обёрнут в `using` (хендл закрывается сразу,
  сам explorer/rundll32 живёт дальше).
- **Мёртвая команда `OpenContainingFolder`.** Не привязана нигде в UI — удалена команда,
  строка в `RefreshCommandStates`, метод из `IShellService`/`ShellService`.
- **`SeekStepSeconds`: три разных предела.** Модель `[Range(1,3600)]`, `Commit` clamp 120, слайдер 30.
  Сведено к единому `[1, 30]` (диапазон слайдера).

Тесты: 85 (добавлен кейс на новую границу `SeekStepSeconds`).

### 5.24. Оркестрированный аудит P5 (цикл 4) — добивка после восстановления лимитов

Четвёртый цикл (полный свип 10 измерений). Подтверждено и исправлено 5 дефектов:

- **Слайд-шоу зависало на видео с ошибкой.** `OnSlideshowTick` пропускал тик при
  `VideoViewerViewModel { IsEnded: false }`, но видео с `HasError` никогда не поднимает
  `EndReached` → `IsEnded` навсегда false → показ застревал на битом файле. Условие дополнено
  `HasError: false` (пробел в фиксе цикла 2 5.23 — учитывался только `IsEnded`).
- **Нет process-wide обработчиков исключений.** Был только `DispatcherUnhandledException`
  (UI-поток). Фоновые сбои (fire-and-forget задачи, continuations, STA-потоки) не логировались.
  Добавлены `TaskScheduler.UnobservedTaskException` (с `SetObserved`) и
  `AppDomain.CurrentDomain.UnhandledException` → `LogCrash`.
- **`OpenDefaultAppsSettings` без try/catch.** `ms-settings:` мог быть недоступен (политика) →
  `Win32Exception` всплывал в `DispatcherUnhandledException` (пугающее окно). Обёрнут в
  try/catch + `using` (как в `ShellService`).
- **Регистрация ассоциаций из UI без обработки.** `OnIntegrateShellChanged`/`ToggleAssociations`
  вызывали `Register`/`Unregister` без try/catch (в ограниченной среде — креш + рассинхрон
  тумблера). Обёрнуто в try/catch с `AppLog.Error` и ресинхронизацией `IsAssociationsRegistered`.
- **Мёртвый член `IShellService.OpenUri`.** Не вызывался нигде — удалён из интерфейса и реализации.

Циклы 1-4 P5 суммарно: **27 подтверждённых фиксов** + 1 lifecycle-guard. Тесты: 85 зелёных.

### 5.25. Оркестрированный аудит P5 (цикл 5) — кэш-когерентность, UI-I/O, init-порядок

Пятый цикл (полный свип). Подтверждено и исправлено 4 дефекта:

- **Поворот не виден после сохранения (кэш-когерентность).** После `SaveRotationAsync`
  перезаписывал файл, но `LoadAsync` через `_cache.TryGetLoaded` отдавал СТАРУЮ (неповёрнутую)
  копию из singleton `ImageCache` — поворот «исчезал» в UI, хотя на диске файл корректный.
  Добавлен `IImageCache.Invalidate(path)` (→ приватный `RemoveEntry` под `_gate`), вызывается
  в `SaveRotationAsync` сразу после `File.Move`. Покрыто `ImageCacheTests`.
- **Синхронный файловый I/O на UI-потоке при навигации.** Диагностические `[Perf]`-логи
  (`AppLog.Write`) в `UpdateCurrentContent` (КАЖДАЯ навигация) и `OpenPathAsync`/`ResolveOrderingAsync`
  (каждое открытие) делали `File.AppendAllText` под глобальным lock прямо на UI-потоке.
  Удалены (это была временная инструментовка фаз оптимизации; стартовые `[Perf]`-логи в
  `App`/`LibVlcProvider`, срабатывающие однократно, оставлены).
- **`LibVlcProvider.Warmup` создавал LibVLC до `Core.Initialize()`** на первом холодном старте
  (ветка генерации `plugins.dat` вне lock). `Core.Initialize()` настраивает путь к нативным
  `libvlc\win-x64\`. Добавлен вызов `EnsureCoreInitialized()` в начале `Warmup`;
  `EnsureCoreInitialized` сделан lock-safe (re-entrant через `_gate`).
- **`OpenPathAsync` применял побочные эффекты до проверки отмены.** `_recent.Add`/`LastFilePath`/
  `ReflectSort` выполнялись до единственной проверки `ct.IsCancellationRequested` (перед `SetItems`).
  При гонке быстрых открытий устаревший вызов рассинхронизировал recent/индикатор сортировки.
  Проверка отмены добавлена сразу после каждого `await _library.Build...Async` — до побочных эффектов.

Циклы 1-5 P5: **31 подтверждённый фикс** + ToastView-guard. Тесты: 88 зелёных
(добавлены `ImageCacheTests`). Тенденция severity по циклам: HIGH/MEDIUM → MEDIUM/LOW → MEDIUM/LOW
→ снова всплеск HIGH в цикле 5 (кэш-когерентность, UI-I/O) — свип ещё находит значимое.

### 5.26. Оркестрированный аудит P5 (цикл 6) — resume-позиция, кэш-память, shutdown-disposal

Шестой цикл (полный свип). Подтверждено и исправлено 3 дефекта:

- **`SavePosition()` в `Dispose` был no-op → resume-позиция терялась.** `Dispose` ставил
  `_disposed = true` ДО `SavePosition()`, а та имеет ранний return при `_disposed`. Поэтому при
  video→фото/пусто и закрытии на видео позиция не сохранялась (функция `ResumeVideoPosition`
  молча не работала в самых частых сценариях). Порядок переставлен: `SavePosition()` до
  `_disposed = true`. **Важно:** добавлен guard `File.Exists(Item.FullPath)` — иначе отложенный
  `Dispose` удалённого видео воскресил бы resume-запись, которую `Delete` уже убрал (взаимодействие
  с фиксом цикла 2 5.23).
- **`ImageCache`: учёт памяти запаздывал.** `EstimatedBytes` ставился асинхронно в `ContinueWith`
  ПОСЛЕ синхронного `Trim()` (когда размер ещё 0) → байтовый лимит (800 МБ) недосчитывал крупные
  in-flight записи (при `Preload` соседей — всплеск памяти). Теперь `Trim()` перевызывается внутри
  `ContinueWith` под `_gate` после установки фактического `EstimatedBytes`.
- **VM ушедшего видео освобождался с `Background`-приоритетом → мог не успеть до выгрузки LibVLC**
  при закрытии (видео→фото + немедленное закрытие) → осиротевшие `MediaPlayer`/`Media` против
  уже освобождённого LibVLC (риск AV). `MainViewModel` запоминает `_pendingDisposal` и довершает
  его синхронно в `Dispose` (идемпотентно) — до выгрузки хоста.

Циклы 1-6 P5: **34 подтверждённых фикса** + ToastView-guard. Тесты: 88 зелёных.
Severity по циклам: …→2H(ц5)→1M+2L(ц6) — пик пройден, тренд к затуханию.

### 5.27. Оркестрированный аудит P5 (цикл 7) — disposal-гонка событий, рассинхрон удаления

Седьмой цикл. Подтверждено и исправлено 3 дефекта (2 HIGH):

- **Обработчики событий плеера без `_disposed`-guard → запись в уничтоженный MediaPlayer.**
  `OnUi` ставит лямбды через `BeginInvoke` (асинхронно); отписка в `Dispose` не отменяет уже
  поставленные. `OnTimeChanged`/`OnLengthChanged` имели `if (!_disposed)`, но `OnPlaying`
  (пишет `Rate`/`Volume`/`Mute` в нативный плеер!), `OnPaused`/`OnStopped`/`OnError`/`OnEndReached`
  — нет. Гонка при video→фото/видео/закрытии → AccessViolation. Добавлен `if (_disposed) return;`
  во ВСЕ обработчики.
- **Удаление при включённом подтверждении: рассинхрон файла и записи списка.** `cur` фиксировался
  ДО `await ConfirmAsync`, индекс — ПОСЛЕ. `ConfirmAsync` (Wpf.Ui MessageBox) не блокирует помпу
  и не подавляет хоткеи → пользователь стрелками/миниатюрой менял файл во время диалога →
  удалялся файл A с диска, но из списка выкидывался текущий B. Теперь индекс берётся по `cur`
  через новый `INavigationService.IndexOf(cur)` ПОСЛЕ удаления; при `index < 0` запись не трогаем.
- **Мёртвое свойство `ShowNavigation`** (дублировало `ShowWindowNavArrows`/`ShowFileNavigation`) —
  удалено вместе с лишней `OnPropertyChanged` в `OnListChanged`.

Циклы 1-7 P5: **37 подтверждённых фиксов** + ToastView-guard. Тесты: 90 (добавлены `IndexOf`).
Severity: цикл 5 (2H) → 6 (1M+2L) → 7 (2H) — свип ещё находит значимое (глубокие async/disposal-гонки).

### 5.28. Оркестрированный аудит P5 (цикл 8) — живое применение настроек

Восьмой цикл. Подтверждён и исправлен 1 дефект (HIGH):

- **Настройки интерфейса не применялись вживую.** `SettingsChanged` поднимается только из
  `ISettingsService.Save()`, а `SaveDebounced()` (тихий путь для частых volume/recent) — нет.
  `OnShowThumbnailsChanged`/`OnThumbnailStripPositionChanged`/`OnSlideshowIntervalSecondsChanged`
  шли через `Commit()`→`SaveDebounced()`, поэтому `MainViewModel.OnSettingsChanged` (подписан на
  `SettingsChanged`) не вызывался: выключение «Показывать миниатюры», смена положения ленты и
  интервала слайд-шоу не применялись до следующего `ListChanged`/смены темы/перезапуска. Помечены
  `Commit(immediate: true)` (как тема) — `Save()` поднимает `SettingsChanged` синхронно на UI-потоке.
  `AutoHideControls` остаётся debounced (читается «на лету» в таймерах, событие не нужно).

Циклы 1-8 P5: **38 подтверждённых фиксов** + ToastView-guard. Тесты: 90 зелёных.
Подтверждённых по циклам: 10→11→0→5→4→3→3→1 — отчётливый тренд к затуханию.

### 5.29. Оркестрированный аудит P5 (цикл 9) — статус после пересортировки

Девятый цикл (totalRaw=1 — finders почти исчерпаны). Подтверждён и исправлен 1 дефект (MEDIUM):

- **Счётчик «N из M» не обновлялся после пересортировки.** `UpdateStatus()` вызывался только из
  `UpdateCurrentContent` (по `CurrentChanged`), а `ReorderPreservingCurrent` поднимает только
  `ListChanged` (хотя `_index` меняется — файл переезжает). Статус-строка и инфо-плашка показывали
  устаревший номер до следующей навигации. Добавлен `UpdateStatus()` в `OnListChanged`
  (идемпотентен; дублирование с `UpdateCurrentContent` безвредно).

Циклы 1-9 P5: **39 подтверждённых фиксов** + ToastView-guard. Тесты: 90 зелёные.
Подтверждённых по циклам: 10→11→0→5→4→3→3→1→1; сырых в цикле 9 — всего 1. Сходимость близка.

### 5.30. Оркестрированный аудит P5 (цикл 10) — СХОДИМОСТЬ

Десятый цикл (полный свип 10 измерений + адверсариальная верификация) дал **0 находок** —
все finder-агенты отработали и не нашли значимых дефектов. Совместно с циклом 9 (всего 1 сырая
находка) это подтверждает **сходимость loop-until-dry**.

**Итог P5 (циклы 1–10):** 39 подтверждённых фиксов + ToastView-guard, 10 коммитов.
Тренд подтверждённых: 10→11→0→5→4→3→3→1→1→**0**. Сырых в последних циклах: …→4→1→0.

> При дальнейших изменениях кода стоит снова прогнать оркестрированный аудит (скрипты в
> `.claude/.../workflows/scripts/prosmotr-audit-cycleN-*.js`) — он быстро ловит гонки/disposal/
> кэш-когерентность, которые не видны при обычном ревью.

**Последующая локальная правка (после сессии):** настройка аппаратного ускорения видео и
связанный downgrade `VideoLAN.LibVLC.Windows` 3.0.23.1 → 3.0.21 были временно внесены для диагностики
белого мерцания при переключении видео. Они не решили проблему и были полностью откачены.
Текущая версия файлов возвращена к исходному состоянию P5 (LibVLC 3.0.23.1, нет
`UseHardwareDecoding`, нет соответствующего тоггла в настройках). Решение для фото — JPEG через
Magick.NET с нормализацией ICC-профилей (см. §5.5).

Тесты: 104 unit-теста (xUnit, `tests/Prosmotr.Tests`), все зелёные. Сборка 0/0.

### 5.31. Скроллбар ленты миниатюр

Лента миниатюр (`ThumbnailStripView`) использует `ListBox` внутри `ScrollViewer`. Важные нюансы
при доработке скроллбара:

- **Не заменяй полный шаблон `ScrollBar`.** Системный шаблон WPF-UI содержит корректную логику
  `Track` и `RepeatButton`. Замена всего `ControlTemplate` ломает перетаскивание бегунка и клики
  по дорожке. Достаточно стилизовать только `Thumb` (`ThumbnailScrollThumbStyle`).
- **Размеры и форма бегунка настраиваются в code-behind.** В `ThumbnailStripView.xaml.cs`
  после загрузки визуального дерева (`Loaded` / `OrientationChanged`) находятся `ScrollBar` и
  `Thumb`, увеличиваются толщина трека (`ScrollBarThickness = 28`), минимальная длина бегунка
  (`MinThumbLength = 72`) и максимальная доля трека (`MaxThumbRatio = 0.55`). Длина бегунка
  также зависит от числа файлов в папке.
- **Клик по дорожке — прыжок в точку.** Обработчик `PreviewMouseLeftButtonDown` на `ScrollBar`
  вычисляет позицию клика относительно `Track` и устанавливает `HorizontalOffset`/`VerticalOffset`
  через `ScrollViewer`. Событие помечается `Handled = true`, чтобы `ListBoxItem` не получил
  активацию и файл не открылся.
- **Шаблон `Thumb` задаёт ровные скругления.** `CornerRadius="6"` со всех сторон убирает
  каплевидную/заострённую форму системного бегунка. Фон — акцентный цвет темы
  (`AccentFillColorDefaultBrush`) с непрозрачностью `0.95`, чтобы бегунок был заметен на любом
  фоне.
- **Высота ленты увеличена в `MainWindow.xaml`** (`Height="112"`), чтобы горизонтальный
  скроллбар не перекрывал широкие превью видео.
- **Плавная прокрутка.** У `ListBox` убран `ScrollViewer.CanContentScroll="True"`, поэтому
  прокрутка пиксельная, а бегунок масштабируется естественным образом.

### 5.32. Picture-in-Picture

- PiP перемещает тот же `MediaPlayer` из основного `VideoView` в плавающее окно. При возврате плеер привязывается обратно; используется тот же чёрный cover + `DispatcherPriority.Render`, что и при обычном переключении видео, чтобы скрыть белый фон нативного HWND.
- При входе в PiP основной `VideoViewerView` отключается, но плеер **не останавливается и не освобождает `Media`** — иначе в плавающем окне остался бы белый/чёрный экран без дорожки. Проверка `_vm.IsPictureInPicture` в `VideoViewerView.OnUnloaded` пропускает `StopAndRelease`.
- При возврате из PiP в основное окно `VideoViewerViewModel.RestoreFromPictureInPicture`
  сбрасывает guard `_started`, устанавливает флаг `_resumeFromPip` и поднимает
  `IsBuffering`. Когда `VideoViewerView` вызывает `Start()`, по флагу выполняется
  `_playback.Stop()` + `_playback.Play()` вместо полной перезагрузки дорожки. Это
  пересоздаёт нативный vout LibVLC для HWND основного `VideoView` (а не оставляет
  его привязанным к PiP-окну), поэтому картинка появляется в основном режиме и
  позиция не теряется. Статус play/pause, накопленный в PiP, тоже сохраняется:
  если видео было на паузе, после `Play()` вызывается `Pause()`, иначе продолжает
  играть.
- При входе в PiP сохраняются не только позиция, но и статус play/pause основного
  окна. В `PictureInPictureWindow.ShowFor` после `Load` всегда вызывается `Play()`,
  чтобы плеер создал vout для HWND PiP-окна; затем, если видео было на паузе,
  сразу вызывается `Pause()`, иначе продолжает играть.
- Восстановление (`RestoreRequested`) и закрытие (`CloseRequested`) PiP обрабатываются в `MainViewModel`, а не рекурсивно через `RaiseRestore`. Ранее обработчик Restore вызывал `RaiseRestore()` снова → бесконечная рекурсия и падение приложения при возврате видео в основное окно.
- Горячая клавиша `P` включает/выключает PiP только когда текущий контент — видео или активно PiP-окно.
- При закрытии основного окна PiP закрывается автоматически через `MainViewModel.Dispose`.
- Если удаляемый файл воспроизводится в PiP, PiP закрывается перед удалением, чтобы освободить файловый handle.
- PiP-окно без рамки (`WindowStyle=None`), `Topmost=true`, `ShowInTaskbar=true` (отдельная кнопка на панели задач), размер по умолчанию 400×225 DIP, минимум 240×135, максимум 720×405. Перемещается за любую точку окна при зажатой ЛКМ. Мини-панель появляется при движении мыши и прячется через 3 с бездействия.

### 5.33. Превью кадра при наведении на таймлайн

При наведении на таймлайн видео (и при перетаскивании бегунка) над слайдером показывается миниатюра кадра
на позиции под курсором + метка времени — как на YouTube. Основной плеер при этом не трогается:

- **Второй «скрытый» декодер** — `Services/VideoFramePreviewService` (создаётся `VideoViewerViewModel` лениво,
  при первом наведении; НЕ в DI). Отдельный `MediaPlayer` на том же `LibVLC` выводит видео **в память** через
  `SetVideoFormatCallbacks` + `SetVideoCallbacks` (без окна/HWND и без временных файлов). В format-колбеке
  форсируется хрома **RV32** (B,G,R,A, 4 байта/px), кадр масштабируется до **≤320px по ширине** с сохранением
  пропорций, `pitches`/`lines` выравниваются **кратно 32** (требование libvlc). Медиа — с опцией `:no-audio`;
  `EnableHardwareDecoding = false` (консистентно с основным плеером).
- **Сигнатуры делегатов LibVLCSharp 3.9.7.1** (проверено по исходникам тега): lock `IntPtr(IntPtr, IntPtr)` — буфер
  задаётся `Marshal.WriteIntPtr(planes, ptr)`; format `uint(ref IntPtr, IntPtr, ref uint ×4)` — **возвращает число
  буферов-картинок, которое даст lock-колбек (1); 0 = отказ, vmem не запускается** («video format setup failure
  (no pictures)»); display `void(IntPtr, IntPtr)`.
  `SetVideoFormatCallbacks` и `SetVideoFormat(string,uint,uint,uint)` взаимоисключаемы — используется только callbacks-вариант.
  Проверено headless-тестом на реальных видео: кадры отдаются за ~100–130 мс (первый) и <40 мс (последующие, буфер
  переиспользуется), масштаб сохраняет пропорции источника.
- **Превью-плеер живёт в состоянии паузы**: после первичного `Play(media)` через ~200 мс вызывается `SetPause(true)`;
  `Time = ms` на paused-плеере перерисовывает кадр новой позиции (как покадровый шаг). На некоторых кодек/контейнерах
  paused-seek не отдаёт кадр — **fallback**: короткий `Play()` → `SetPause(true)` (`:no-audio`, звук не пострадает);
  если и это не дало кадра за таймаут (~2 с) — возвращается `null`, превью просто не показывается (мягкая деградация).
- **Дедлок-риск `_sync`:** `Stop()`/`Play()`/`Dispose()` плеера вызываются **ВНЕ** `lock (_sync)` — vout-поток
  блокируется на этом lock в колбеках (`OnLock`/`OnDisplay`/`OnFormat`), а `Stop` ждёт завершения vout.
- **«Только последний запрос важен»**: `VideoViewerViewModel.RequestPreviewFrameAsync(ms)` отменяет предыдущий
  запрос (CTS + поколение `_previewGen`), конвертирует кадр в `BitmapSource` на UI-потоке (`MediaRendering.PixelFormats.Bgra32`)
  и `Freeze()`. View (`VideoViewerView`) семплирует ховер каждые **150 мс** (`_previewThrottle`) и гонится за
  курсором серийно: **не более одного запроса одновременно** (`_previewRequestInFlight`), по завершении сразу
  запрашивается кадр на актуальной позиции (цепочка `PumpPreviewFrame`). Первый кадр запрашивается немедленно
  при наведении (leading edge). Так превью обновляется непрерывно во время движения (в темпе декодирования),
  а не только после остановки мыши — дебаунс-вариант «по остановке» давал залипание картинки на первом кадре.
- **Освобождение файлового handle**: `VideoFramePreviewService.ReleaseMedia()` вызывается в `StopAndRelease`
  (удаление — иначе `IFileOperation` не переместит файл, sharing violation) и в `SwitchTo` (старый файл);
  новый файл поднимается лениво при следующем наведении. `Dispose` VM освобождает экстрактор до `_playback.Dispose()`.
- **Настройки** (раздел «Видео»): `ShowTimelinePreview` (default **true**, общий тумблер) и `TimelinePreviewPauseVideo`
  (default **false** — режим «пауза при наведении», как Windows Photos). Оба — `Commit(immediate: true)`;
  `VideoViewerViewModel` поднятает `PropertyChanged` для `TimelinePreviewEnabled`/`PauseVideoOnHover` из `SettingsChanged`.
  Режим паузы: `PauseForPreview()`/`ResumeFromPreview()` в VM, в View наведение с задержкой **250 мс** (`_pauseHoverTimer`,
  чтобы случайное пересечение курсора не ставило паузу); возобновление учитывает, играло ли видео до наведения.
- **Позиционирование**: `PreviewPanel` (Border 328×208, Image 320×180 + метка времени) в оверлее (`Grid.Row=0`),
  `VerticalAlignment=Bottom`, отступ снизу = высота `ControlBar` + 10px, по X следует за курсором с clamp по ширине окна
  (у краёв не вылезает). Скрывается в `UpdateChromeVisibility` вместе с панелью управления/буферизацией,
  при `MouseLeave` слайдера, при смене контента и в `OnUnloaded`.
- **Чистая функция** `Infrastructure/TimelineMath.MapSliderXToMs(x, width, lengthMs)` — X слайдера → мс (покрыта
  юнит-тестами, `TimelineMathTests`).
- **Границы:** превью — только основной таймлайн видео; PiP-окно и мини-таймлайн превью НЕ показывают (вне скоупа).
- **CPU:** каждый наведённый seek декодирует один кадр (софтверно, в фоне vout-потока); серийная погоня
  (не более одного запроса за раз) + таймер 150 мс не дают потока seek'ов.

---

## 6. Соглашения по работе (важно)

Глобальные правила пользователя (`~/.claude/CLAUDE.md`) действуют и здесь:

- 🔄 **Держи `AGENTS.md` актуальным.** При **каждом** изменении проекта проверь, не устарел ли этот
  файл, и дополни/исправь его в рамках того же изменения. Обновлять нужно, в частности, при:
  - новых или изменённых **подводных камнях/нюансах** (раздел 5) — это главная ценность файла;
  - изменении **архитектуры**, структуры каталогов, добавлении/удалении сервисов, VM, экранов (раздел 4);
  - изменении **команд** сборки/запуска/публикации или процесса работы с папкой `app\` (разделы 2–3);
  - смене **зависимостей**, версий пакетов, TFM, целевой платформы (раздел 1, csproj);
  - изменении путей **хранения данных/логов**, настроек, форматов файлов.

  Если правка делает какой-то нюанс в этом файле неверным — не оставляй устаревший текст, исправь его.
  Цель: открыв `AGENTS.md`, агент всегда видит правду о проекте.
- 📦 **После любых правок кода или XAML обязательно публикуй в `app\`** (`dotnet publish … -o app`),
  иначе изменения не дойдут до приложения, которое запускает пользователь ярлыком (см. раздел 3.1).
  Просто `dotnet build`/`run` недостаточно — ярлык их не видит. Это завершающий шаг почти любой задачи.
- **Не коммитить и не пушить без явного подтверждения** пользователя именно для этого изменения.
  Все правки остаются в рабочем дереве, пока он не скажет «коммить»/«пуш»/«отправь». Сборка,
  публикация в `app\`, проверки — можно без спроса (это не трогает git).
- **Не добавлять trailer `Co-Authored-By: Claude …`** в коммиты.
- Стиль кода: следуй окружающему коду. Комментарии в проекте — на русском, по делу
  («почему», а не «что»). Nullable и ImplicitUsings включены.

---

## 7. Как проверять изменения UI

Это GUI-приложение; быстрый способ убедиться, что экран рисуется правильно:

1. Закрой запущенные экземпляры: `Get-Process Prosmotr | Stop-Process -Force`.
2. **Обязательно переопубликуй в `app\`** — без этого изменения не увидит ни ярлык, ни пользователь
   (XAML тоже компилируется в `Prosmotr.dll`, поэтому одной пересборки `bin\` мало):

   ```powershell
   dotnet publish src\Prosmotr\Prosmotr.csproj -c Release -o app
   ```

3. Запусти `app\Prosmotr.exe` (опц. с путём к медиафайлу как аргументом).
4. Сделай скриншот окна. Окно может быть перекрыто другими — надёжнее `PrintWindow` (флаг
   `PW_RENDERFULLCONTENT = 2`) по `MainWindowHandle`, чем `CopyFromScreen`.
5. Для проверки граничных случаев меняй размер окна (`MoveWindow`) — например, маленькое окно
   выявляет проблемы с прокруткой/обрезкой.
6. **Мини-таймлайн:** открой видео короче 20 мин, дождись автоскрытия панели — внизу появится
   тонкая полоска прогресса, заполняющаяся по мере воспроизведения. Двинь мышь — панель управления
   появляется, мини-полоска исчезает. Видео длиннее порога (по умолчанию 20 мин) — полоски нет.
   Проверь настройки: отключение тумблера «Мини-таймлайн при скрытой панели» убирает полоску;
   увеличение порога до 30 мин заставляет 25-минутное видео показывать её. На паузе полоски нет.
7. **Превью при наведении на таймлайн:** наведи курсор на таймлайн — над ним появится миниатюра кадра
   с меткой времени, видео продолжает играть. Подвигай мышь — кадр меняется (с задержкой до ~2 с на
   длинно-GOP видео). Перетащи бегунок — превью следует за позицией, отпускание перематывает как раньше.
   Проверь у краёв окна — панель не вылезает за границу. Включи «Пауза при наведении» — наведение ставит
   видео на паузу, уход мыши возобновляет (если видео и так было на паузе — остаётся). Выключи тумблер
   «Превью при наведении» — ничего не показывается. В PiP-окне и на мини-таймлайне превью нет (вне скоупа).
