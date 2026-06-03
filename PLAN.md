# План устранения недостатков: «Просмотр»

**Версия плана:** 1.0  
**Дата:** 2026-06-03  
**Статус:** черновик (ожидает согласования)

---

## Критерии приоритета

| Приоритет | Что покрывает |
|-----------|---------------|
| **P0 (Hotfix)** | Утечки памяти, зависания UI, data-loss, AV при типовых сценариях |
| **P1 (Stability)** | Ресурсные утечки, неотменённые операции, некорректное поведение при граничных случаях |
| **P2 (Performance & UX)** | Фризы, лишний IO, UX-баги, accessibility |
| **P3 (Architecture & Polish)** | Техдолг, P/Invoke, тестируемость, читаемость |

> **Правило применения:** после завершения каждой фазы обязательна публикация в `app\` (`dotnet publish … -o app`) и проверка через ярлык (см. AGENTS.md §3.1, §6).

---

## Фаза 0 — Hotfix (P0)

Цель: устранить то, что портит типовой сценарий (открыл → посмотрел → закрыл).

| # | Задача | Файл(ы) | Что делать | Критерий готовности |
|---|--------|---------|------------|---------------------|
| 0.1 | **Утечка `EmptyStateViewModel`** ✅ | `MainViewModel.cs` → `UpdateCurrentContent()` | Проверять `old is IDisposable`, а не конкретные типы (`ImageViewerViewModel`/`VideoViewerViewModel`) | После открытия файла из стартового экрана `EmptyStateViewModel` освобождается (`RecentFilesService.Changed` отписан) |
| 0.2 | **Залипание панорамы в `ZoomBorder`** ✅ | `Views/Controls/ZoomBorder.cs` | Переопределить `OnLostMouseCapture`: сбрасывать `_dragging = false`, `Cursor = Cursors.Arrow`, `ReleaseMouseCapture()` | Если во время перетаскивания мышь уходит за край окна и отпускается там, при возвращении изображение не «ползёт» за курсором |
| 0.3 | **Остаток `.tmp` при ошибке сохранения поворота** ✅ | `ViewModels/ImageViewerViewModel.cs` → `SaveRotationAsync()` | Обёрнуть `File.Move` в `try/finally { try { File.Delete(tmp); } catch {} }` | При исключении на этапе `encoder.Save` или `File.Move` временный `.tmp` удаляется |
| 0.4 | **Нормализация пути в восстановлении из Корзины** ✅ | `Infrastructure/RecycleBinRestore.cs` → `MatchesPath()` | Использовать `Path.GetFullPath` (или `TrimEnd('\')`) для `full` и `originalPath` перед сравнением | Файл восстанавливается даже если в пути расходятся конечные слэши |

---

## Фаза 1 — Stability & Safety (P1)

Цель: приложение не копит потоки/хэндлы при длительной работе и корректно освобождает нативные ресурсы.

| # | Задача | Файл(ы) | Что делать | Критерий готовности |
|---|--------|---------|------------|---------------------|
| 1.1 | **Cleanup transient VM при shutdown** | `App.xaml.cs` → `OnExit()`, `MainViewModel.cs` | Добавить `MainViewModel.Dispose()` (или `Shutdown()`), который вызывает `(CurrentContent as IDisposable)?.Dispose()` перед `_host?.Dispose()` | При закрытии окна на видео `MediaPlayer` и `Media` освобождаются до выгрузки LibVLC |
| 1.2 | **Отмена фоновых задач `ImageCache`** | `Services/ImageCache.cs` | Передавать `CancellationToken` в `_decoder.LoadAsync` и отменять/удалять задачи при вытеснении из LRU (см. `Trim()`) | При быстром листании тяжёлых файлов декодирование старых отменяется; в логе не растёт число `UnobservedTaskException` |
| 1.3 | **Вынести `ExplorerSortReader` из UI-потока** | `ViewModels/MainViewModel.Gallery.cs` → `ResolveOrderingAsync()` | Оборачивать вызов `ExplorerSortReader.TryGetOrderedPaths` в `await Task.Run(...)` | Открытие папки с включённым `MatchExplorerSort` не фризит окно, даже если Explorer занят |
| 1.4 | **Debounced сохранение настроек из `SettingsViewModel`** | `ViewModels/SettingsViewModel.cs` → `Commit()` | Заменить `_settings.Save()` на `_settings.SaveDebounced()` во всех `OnXxxChanged`, кроме `Theme` | Движение ползунка «Шаг перемотки» не пишет JSON 30 раз за секунду |
| 1.5 | **Обработка `UriFormatException` в плеере** | `Services/VideoPlaybackService.cs` → `Load()` | Заменить `new Uri(path)` на `new Uri(path, UriKind.Absolute)` в try/catch; при `UriFormatException` экранировать или использовать `FromType.FromPath` | Видео с `#` или `%` в пути открывается корректно |
| 1.6 | **Экранирование пути в `ShellService`** | `Services/ShellService.cs` | Оборачивать `path` в кавычки и экранировать внутренние кавычки: `path.Replace("\"", "\\\"")`, затем `"/select,\"{path}\""` | Путь `C:\My"Folder\file.jpg` открывается в Explorer без разбора аргументов |

---

## Фаза 2 — Performance & UX (P2)

Цель: сделать прокрутку больших папок плавной, а настройки — безопасными.

| # | Задача | Файл(ы) | Что делать | Критерий готовности |
|---|--------|---------|------------|---------------------|
| 2.1 | **Батчевание обновлений миниатюр** ✅ | `ViewModels/ThumbnailStripViewModel.cs` → `LoadThumbnailsAsync()` | Вместо `InvokeAsync` на каждый файл — накапливать готовые миниатюры в список и сбрасывать пачкой раз в 50–100 мс через `DispatcherTimer` или `Channel` | Папка из 5000 файлов: лента прокручивается без подвисаний, CPU не уходит в `Dispatcher.Invoke` |
| 2.2 | **Ограничение памяти `ImageCache`** ✅ | `Services/ImageCache.cs` | Добавить опциональный лимит по памяти: при превышении 800 МБ суммарно полноразмерных BitmapSource — вытеснять самые старые, независимо от `Capacity` | При листании тяжёлых RAW/HEIC процесс не раздувается >1 ГБ RAM |
| 2.3 | **Валидация конфликтов горячих клавиш в настройках** ✅ | `ViewModels/SettingsViewModel.cs`, `Views/MainWindow.xaml.cs` | В `SettingsViewModel` при смене `ExitKey`/`ToggleChromeKey` проверять, что ключ не конфликтует с `Delete`, `Left`, `Right`, `Space`; показывать предупреждение в UI (или убирать конфликтующие из списка) | Нельзя выбрать `Delete` как клавишу закрытия программы |
| 2.4 | **Остановка анимации GIF при смене файла** ✅ | `Views/ImageViewerView.xaml.cs` | В `OnDataContextChanged`/`DetachVm` сбрасывать `AnimationBehavior.SetSourceUri(AnimatedImage, null)` | При переходе с GIF на JPG декодер GIF останавливается; не растёт потребление CPU/GDI |
| 2.5 | **Ротация `AppLog`** ✅ | `Infrastructure/AppLog.cs` | При старте: если `app.log` > 10 МБ — переименовать в `app.log.old` (одна резервная копия) | Лог не разрастается бесконечно |
| 2.6 | **Исправление «висящих» значений в свойствах видео** ✅ | `ViewModels/FilePropertiesViewModel.cs` → `SetVideoUnavailable()` | Убрать условие `&& _durationRow.Value == "…"`; всегда сбрасывать все поля видео на `"—"` при ошибке | При таймауте `Media.Parse` поля разрешения/длительности показывают `"—"`, а не устаревшие значения |
| 2.7 | **Улучшение `Task.Yield()` в ленте** ✅ | `ViewModels/ThumbnailStripViewModel.cs` | Заменить `await Task.Yield()` на `await Dispatcher.Yield(DispatcherPriority.Render)` | WPF гарантированно отрисовывает кадр между пачками добавления элементов |

---

## Фаза 3 — Architecture & Polish (P3)

Цель: упростить поддержку, устранить архитектурные запахи, укрепить нативный слой.

| # | Задача | Файл(ы) | Что делать | Критерий готовности |
|---|--------|---------|------------|---------------------|
| 3.1 | **Убрать Service Locator из `ToastView`** | `Views/Controls/ToastView.xaml.cs`, `MainWindow.xaml` | Передавать `INotificationService` через `DataContext` или attached property от `MainWindow` (где есть `_services`). `ToastView` получает сервис в конструкторе или через binding | `ToastView` не обращается к `Application.Current as App` |
| 3.2 | **Заменить `SetWindowLong` на `SetWindowLongPtr` для x64** | `Views/MainWindow.xaml.cs` | Объявить `SetWindowLongPtr` (или обернуть через `IntPtr` корректно) для `GWL_STYLE` | На x64 изменение стиля окна не опирается на UB |
| 3.3 | **Рефакторинг `MainWindow` — уменьшить code-behind** | `Views/MainWindow.xaml.cs` | Вынести логику fullscreen (подкласс, `DwmSetWindowAttribute`, `MonitorFromWindow`) в отдельный `static class FullScreenHelper` в `Infrastructure/`. `MainWindow` вызывает `FullScreenHelper.Apply(window, on)` | `MainWindow.xaml.cs` уменьшается на ~150 строк; fullscreen логика изолирована |
| 3.4 | **Фабрика окон в DI** | `App.xaml.cs` → `ConfigureServices()` | Зарегистрировать `MainWindow` как transient с фабрикой (`services.AddTransient(sp => new MainWindow(...))`) | Контейнер может создавать новое окно, если в будущем потребуется |
| 3.5 | **Обновить `AGENTS.md`** | `AGENTS.md` | Дописать новые подводные камни: про утечку `EmptyStateViewModel`, про батчевание миниатюр, про `LostMouseCapture`, про transient VM на shutdown | Документ актуален; новый агент не повторяет найденные баги |

---

## Порядок выполнения и контрольные точки

```
Неделя 1: Фаза 0 (4 задачи)   → publish → smoke-test
Неделя 2: Фаза 1 (6 задач)    → publish → тест на папке 5000 файлов + проверка закрытия на видео
Неделя 3: Фаза 2 (7 задач)    → publish → проверка памяти (Process Explorer), тест GIF→JPG
Неделя 4: Фаза 3 (5 задач)    → publish → финальный review, обновление AGENTS.md
```

### Соглашения по работе над планом

- Каждая задача оформляется **отдельным изменением** (commit).
- **Не пушить в git** без явного согласия пользователя (см. AGENTS.md §6).
- **После каждой фазы:** `dotnet publish src\Prosmotr\Prosmotr.csproj -c Release -o app`.
- При обнаружении новых проблем в процессе — дополнять `PLAN.md` в рамках того же изменения.
