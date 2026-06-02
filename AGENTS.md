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

### 3.2. Single-file publish ЗАПРЕЩЁН

Single-file ломает загрузку нативных плагинов LibVLC. Публикуй обычным способом (framework-dependent).
После сборки в `…\libvlc\win-x64\` должны лежать `libvlc.dll`, `libvlccore.dll` и папка `plugins\`.
Это отражено комментарием в `Prosmotr.csproj` и в README.

### 3.3. Только x64

Нативные плагины LibVLC грузятся из `libvlc\win-x64`. Процесс обязан быть 64-битным
(`PlatformTarget=x64`, `Prefer32Bit=false`). Не переключай на x86/AnyCPU-32.

### 3.4. Single-instance + передача пути через named pipe

В `App.xaml.cs`: при запуске берётся `Mutex` (`Prosmotr.SingleInstance.v1`). Если экземпляр уже
запущен — новый процесс отправляет путь работающему через named pipe (`Prosmotr.OpenFile.v1`) и
сразу завершается. Поэтому второй запуск с файлом открывает файл **в уже открытом окне**, а не
создаёт новое. При отладке single-instance это сбивает с толку — закрывай прежний экземпляр.

---

## 4. Архитектура

Классический **MVVM** поверх DI-контейнера (`Microsoft.Extensions.Hosting`).

- **Точка входа** — `App.xaml.cs`: строит `IHost`, регистрирует сервисы и VM в `ConfigureServices`,
  показывает `MainWindow`, применяет тему, разбирает аргументы, single-instance, интеграция с shell.
- **`MainViewModel`** — главный оркестратор: открытие файлов/папок, навигация, удаление,
  полноэкранный режим, слайд-шоу, сортировка. Держит `CurrentContent` (объект-VM текущего экрана).
- **Контент-экраны** выбираются по типу VM в `CurrentContent`, отрисовываются `ContentControl`
  в `MainWindow.xaml` через неявные `DataTemplate` (тип VM → View):
  - `EmptyStateViewModel` → `EmptyStateView` (стартовый экран, когда ничего не открыто);
  - `ImageViewerViewModel` → `ImageViewerView` (фото/GIF);
  - `VideoViewerViewModel` → `VideoViewerView` (видео, оверлей поверх VLC).
- **Сервисы** (все — singletons, см. `App.ConfigureServices`) с интерфейсами в
  `Services/Abstractions/`: библиотека медиа, навигация, удаление, настройки, тема, кэш изображений,
  декодирование, миниатюры, позиции видео, ассоциации файлов, shell-операции, провайдер LibVLC,
  **уведомления** (`INotificationService`/`NotificationService`).
- **Уведомления.** `NotificationService` лишь поднимает событие `Requested` в UI-потоке; рисуют тост
  сами View — контрол `ToastView` (в главном окне и **внутри оверлея видео**, чтобы тост был виден
  поверх airspace VLC). `ToastView` достаёт `INotificationService` из DI приложения (`App.Services`).

### Карта каталогов

```
src/Prosmotr/
  App.xaml(.cs)              — вход, DI, single-instance, ассоциации
  app.manifest               — DPI awareness PerMonitorV2, supportedOS, longPathAware
  Prosmotr.csproj            — TFM, x64, пакеты, запрет single-file
  Models/                    — MediaItem, AppSettings, RecentEntry, перечисления (MediaType, SortField…)
  Services/                  — реализации сервисов
    Abstractions/            — интерфейсы сервисов (IxxxService)
  ViewModels/                — по VM на экран + MainViewModel, ViewModelBase, Messages
  Views/                     — MainWindow, EmptyStateView, ImageViewerView, VideoViewerView,
                               ThumbnailStripView, SettingsWindow, FilePropertiesWindow (окно «Свойства»)
    Controls/ZoomBorder.cs   — кастомный контрол зума/панорамы для фото (см. подводные камни)
    Controls/ToastView       — всплывающие уведомления (тост), см. подводный камень 5.10
  Converters/                — конвертеры XAML (BoolToVis, InverseBoolToVis, …)
  Infrastructure/            — AppLog, SupportedFormats, NativeMethods (Корзина),
                               RecycleBinRestore (отмена удаления), ShellThumbnail,
                               ExplorerSortReader, NaturalStringComparer
  Resources/                 — иконка app.ico, темы (AppResources.xaml)
app/                         — ⚠️ опубликованная копия (в .gitignore), на неё ведёт ярлык
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
- `ImageViewerView.xaml.cs` пересчитывает зум (`Zoom.SetMode(Fit)`) при смене `Image`/DataContext
  через `Dispatcher.BeginInvoke(..., DispatcherPriority.Render)` — чтобы binding успел применить Source.

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

### 5.5. Декодирование изображений: нативный WPF vs Magick

`ImageDecodingService`: WEBP/HEIC/HEIF (`SupportedFormats.RequiresMagick`) декодируются через
Magick.NET (конвертация в PNG в памяти), остальное — нативным WPF (`BitmapImage` + `OnLoad`,
синхронно, чтобы `Width/Height` были доступны сразу). Анимированные GIF рисует **не** этот сервис,
а XamlAnimatedGif прямо во View (`AnimationBehavior.SourceUri`).

`ImageCache` — небольшой LRU (`Capacity = 7`) полноразмерных изображений; используется для
мгновенного переключения между соседними фото (`Preload` соседей). Полный размер = `decodePixelWidth=0`.

### 5.6. Порядок галереи: приоритет источников сортировки

`MainViewModel.ResolveOrderingAsync`: (1) явный выбор пользователя для конкретной папки
(`ManualFolderSorts` в настройках) → (2) реальный порядок открытого окна Проводника
(`ExplorerSortReader`, если включено `MatchExplorerSort`) → (3) глобальная настройка `SortBy`.
Изменение сортировки в UI запоминается для текущей папки и перекрывает Проводник в следующий раз.

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
  `MainViewModel` хранит последний удалённый в Корзину файл и его индекс; восстановление вставляет
  его обратно через `INavigationService.InsertAt`. Только для удаления **в Корзину** (не «навсегда»).
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
- **Настраиваемая клавиша закрытия программы.** Добавлена настройка `ExitKey` (`AppSettings`),
  по умолчанию `End`. Обрабатывается в `MainWindow.TryHandleHotkey` (до `Escape`), парсится через
  `Enum.TryParse<Key>`. Не назначай навигационные клавиши (стрелки, Space) — иначе сломается
  управление видео/фото.
- **Настраиваемая клавиша скрытия/показа элементов управления.** Добавлена настройка
  `ToggleChromeKey` (`AppSettings`), по умолчанию `PageDown`. Для фото переключает
  `MainViewModel.ChromeVisible` (панель + стрелки + курсор), для видео отправляет
  `ToggleChromeMessage` — `VideoViewerView` скрывает/показывает `ControlBar` и боковые стрелки.
  Не назначай навигационные клавиши.

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
    + `DWMWA_WINDOW_CORNER_PREFERENCE = DWMWCP_DONOTROUND` — убирают белые полосы DWM в Windows 11.
  - `WindowStyle` и `ResizeMode` через WPF **не меняются** (иначе `FluentWindow` в
    `OnExtendsContentIntoTitleBarChanged` принудительно сбросит `WindowStyle` обратно
    в `SingleBorderWindow` и рамка вернётся).
  При выходе всё восстанавливается в обратном порядке.
- **Видео: боковые стрелки скрываются вместе с панелью.** Кнопки `PrevFileButton`/`NextFileButton` в
  `VideoViewerView` больше НЕ привязаны к `ShowFileNavigation` в XAML — их видимостью управляет
  code-behind (`UpdateSideNav`): показываются только когда панель видна (`_controlsShown`) И файлов >1.
  Прячутся по тому же `_hideTimer`, что и `ControlBar`.
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
  Поэтому текст и видимость инфо-плашки видео задаются из code-behind через `_mainVm`
  (полученный через `Window.GetWindow(this)` **до** перемещения контента в `ForegroundWindow`).
- **Полноэкранный режим и airspace-окно видео.**
  После `ApplyFullScreen` (где размер/стиль окна меняются через Win32 API) вызывается
  `Dispatcher.BeginInvoke(UpdateLayout, DispatcherPriority.Render)`. Это нужно, чтобы
  `ForegroundWindow` LibVLCSharp.WPF получил событие `LayoutUpdated` и пересчитал позицию
  overlay-окна — иначе панель/инфо-плашка иногда оказываются смещены или не видны.

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
