# Расследование: белый экран / белые квадраты при переключении видео

> Статус: **не решено до конца**. Белые вспышки/квадраты при смене видео сохраняются.
> Этот файл — рабочий черновик со всеми проверенными гипотезами, что сработало, что нет, и что ещё стоит попробовать.

---

## 1. Что наблюдается

- При переключении между видео (особенно в тёмной теме) мелькает **белый экран / белые квадраты**.
- Проявляется в области нативного HWND LibVLC.
- Ранее были попытки исправить: чёрный фон `VideoHost`, отключение `UseLayoutRounding`, `:start-paused`, чёрный `SwitchCover`.
- Полностью проблема не ушла.

---

## 2. Уже сделанные попытки

### 2.1. Чёрный фон `VideoHost` / `VideoView`

**Результат:** убрало белые полосы по краям, но не сам белый квадрат в центре.

Причина: WPF-фон лежит **за** непрозрачным `HwndHost` LibVLC, поэтому не перекрывает его собственный светлый фон класса `"static"`.

### 2.2. `:start-paused`

В `VideoPlaybackService.Load` добавлена опция `:start-paused`.

**Результат:** уменьшило вспышку, но не убрало полностью. Окно LibVLC существует до первого кадра.

### 2.3. Чёрный `SwitchCover` в оверлее

Добавлен `Border x:Name="SwitchCover"` внутри `ForegroundWindow` (оверлей `VideoView`).

Изначально cover занимал только `Grid.Row="0"`, то есть не перекрывал нижнюю панель управления.

**Результат:** убрал белый квадрат в верхней части, но при видимой панели управления белый фон просвечивал через полупрозрачную панель (`#D91A1A1A`) снизу.

### 2.4. `Grid.RowSpan="2"` для `SwitchCover`

Сделан cover на весь оверлей (`RowSpan="2"`) + скрытие панели управления на время буферизации.

**Результат:** теоретически должен перекрывать всё. На практике пользователь сообщает, что белые квадраты остаются.

### 2.5. Отложенная загрузка `Media` / `Play`

В `VideoViewerViewModel` добавлен `LoadAndPlayDeferred` с `DispatcherPriority.Background`, чтобы cover отрисовался раньше смены медиа.

Также в `VideoViewerView.OnDataContextChanged` / `OnLoaded` привязка `Video.MediaPlayer` и вызов `vm.Start()` вынесены на `DispatcherPriority.Render`, чтобы сначала отрисовался cover.

**Результат:** сокращает окно гонки, но не устраняет его полностью: WPF-оверлей и нативное HWND LibVLC рисуются в разных render-потоках, cover может закраситься позже белого фона HWND.

### 2.6. `CompositionTarget.Rendering` + 50 мс задержка + `StopAndRelease()` в `SwitchTo`

Новая попытка (17.06):
- `LoadAndPlayDeferred` теперь ждёт события `CompositionTarget.Rendering` (гарантированный render-кадр), затем 50 мс, и только потом `Load`/`Play`.
- В `SwitchTo` перед загрузкой нового видео вызывается `_playback.StopAndRelease()`, чтобы HWND очистилось от предыдущего кадра.
- Добавлено логирование `[Flicker]` в `LoadAndPlayDeferred`, `OnPlaying`, `UpdateCover`.

**Результат пользователя (03:39):** приложение запускается сразу, но само видео начинает воспроизводиться с задержкой 5–10 секунд, до этого показывается главная страница. Белые квадраты остались, но вроде стало поменьше.

**Анализ задержки:** видео открывается через `App.OnStartup` → фоновый `LibVlcProvider.Warmup()` → `ContinueWith` → `InitializeAsync(e.Args)`. На холодном старте генерация `plugins.dat` занимает 5–14 секунд, поэтому `InitializeAsync` (и открытие файла) откладывается. Это объясняет лаг 5–10 с перед стартом видео.

### 2.7. Очистка кэша / полная перепубликация

Убиты процессы, удалены `app/`, `bin/`, `obj/`, `%TEMP%\Prosmotr*`, `%TEMP%\.NET*`.  
Проведена `dotnet publish ... -o app` заново.

**Результат:** приложение запускается, видео открываются, но мерцание сохраняется.

---

## 3. Почему стандартные решения не сработали полностью

`LibVLCSharp.WPF.VideoView` состоит из двух HWND:

1. `VideoHwndHost` — собственно окно LibVLC, класс `"static"`, фон по умолчанию светлый.
2. `ForegroundWindow` — WPF-оверлей, внутри которого лежит `Overlay` Grid.

При смене `Player.Media` / вызове `Play()`:

- Старый vout останавливается.
- LibVLC пересоздаёт/перекрашивает нативное HWND своим фоном.
- Первый кадр приходит асинхронно.

WPF-оверлей (`ForegroundWindow`) теоретически всегда поверх нативного HWND, но:

- `ForegroundWindow` имеет `WS_EX_TRANSPARENT` и фон `#02000000` (почти прозрачный).
- Opaque-чёрный `Border` внутри него должен перекрывать всё, но если WPF ещё не завершил render-цикл, а LibVLC уже перекрасил HWND, белый фон всё равно виден.
- `DispatcherPriority.Render`/`Background` уменьшают вероятность, но не гарантируют синхронизацию между WPF-compositor и нативным окном.

Кроме того, при `SwitchTo` (видео→видео) старый и новый Media используют **тот же** `MediaPlayer` и **тот же** HWND. LibVLC может оставить предыдущий кадр/фон на экране до появления нового.

---

## 4. Что ещё стоит попробовать

### 4.1. Временно «затемнить» нативное HWND напрямую через Win32

Цель: установить чёрный фон для `VideoHwndHost`, а не только для WPF-оверлея.

- Получить HWND через `WindowInteropHelper` / отражение `VideoView`.
- Послать `WM_ERASEBKGND`/`WM_PAINT` с `HBRUSH` чёрного цвета.
- Или подклассировать HWND LibVLC и перехватывать `WM_ERASEBKGND`, заливая чёрным.

Сложность: `VideoHwndHost` — `HwndHost`, доступ к его HWND возможен через `HwndSource`, но LibVLC сам управляет стилями и фоном; подкласс может сломать ввод.

### 4.2. Скрывать / отсоединять `VideoView` на время смены видео

Идея: перед `SwitchTo` убирать `Video.MediaPlayer = null` и делать `Video.Visibility = Collapsed` / `Opacity = 0`, пока новое видео не готово.

- `Collapsed` уберёт `HwndHost` из дерева — нативное окно исчезнет.
- После `OnPlaying` возвращаем `Visibility = Visible`.

Минус: будет чёрный экран, но не белый. Возможно мерцание чёрного вместо белого.

### 4.3. Использовать отдельный `MediaPlayer` / `VideoView` для каждого видео

Вместо `SwitchTo` и переиспользования VM создавать новый `VideoViewerViewModel` с новым плеером. Старый VM пусть живёт, пока новый не выдаст `OnPlaying`, затем Dispose старого.

- Новый `MediaPlayer` может инициализироваться «в темноте» (без `VideoView`), пока не готов.
- После `OnPlaying` подменяем `Video.MediaPlayer`.
- Это требует изменений в `MainViewModel.UpdateCurrentContent` и отказа от reuse-логики видео.

### 4.4. Загружать новое видео в `MediaPlayer` без `Play()`, ждать события

Сейчас последовательность: `Load(path)` → `Play()`.  
Можно попробовать: `Load(path)` → `Player.Media = _media` → подписаться на `Player.Playing` → `Play()` только после того, как cover отрисован.

То есть разделить присвоение `Media` и запуск воспроизведения на два Dispatcher-кадра.

### 4.5. Воспользоваться `MediaPlayer.EnableKeyInput = false` + фиксация фокуса

Не связано с мерцанием напрямую, но стоит проверить, что при переключении LibVLC не перехватывает фокус и не вызывает лишних repaint.

### 4.6. Проверить влияние аппаратного декодирования

`EnableHardwareDecoding = false` уже отключено. Попробовать наоборот включить — на некоторых GPU это меняет поведение vout и мерцание.

### 4.7. Включить/выключить `Overlay.Window.AllowsTransparency` и `ForegroundWindow` прозрачность

Возможно, `ForegroundWindow` иногда пропускает один кадр HWND из-за прозрачности.  
Попробовать сделать `ForegroundWindow` непрозрачным на время буферизации (через `Window` свойства `AllowsTransparency` = false и фон Black).

Это требует доступа к `ForegroundWindow` через отражение — в публичном API `LibVLCSharp.WPF` его нет.

### 4.8. Проверить версию `VideoLAN.LibVLC.Windows`

Сейчас используется `3.0.x`. В новых версиях LibVLC / LibVLCSharp могли исправить поведение `HwndHost` фона. Обновление — отдельный риск (плагины, лицензия).

### 4.9. Использовать `MediaPlayer.SetRole` / `Media.AddOption`:no-video-title` / `qt-bgcone`

LibVLC опции, влияющие на фон: `--no-video-title-show` уже есть. Можно попробовать `--video-title-timeout=0`, `--qt-bgcone=0`, `--vout=direct3d11`/`directdraw`.

### 4.10. Логировать фактические HWND и события

Добавить `AppLog.Write`:

- В `OnDataContextChanged`: когда выставляется `IsBuffering`.
- В `LoadAndPlayDeferred`: когда вызывается `Load`/`Play`.
- В `OnPlaying`: когда `IsBuffering` сбрасывается.
- В `UpdateCover`: значение `SwitchCover.Visibility` и `IsBuffering`.
- Время между установкой cover и вызовом `Play()`.

Это поможет понять, действительно ли cover включён до `Play()` и как долго белый квадрат виден.

---

## 5. Текущие изменения в коде

### 5.1. `src/Prosmotr/Views/VideoViewerView.xaml`

```xml
<Border x:Name="SwitchCover" Grid.Row="0" Grid.RowSpan="2" Background="Black"
        IsHitTestVisible="False"
        Visibility="{Binding IsBuffering, Converter={StaticResource BoolToVis}}" />
```

Cover позже всех элементов, кроме `ToastView`. Перекрывает `Grid.RowSpan="2"`.

### 5.2. `src/Prosmotr/Views/VideoViewerView.xaml.cs`

- `OnDataContextChanged`: сначала `UpdateCover()`, затем на `DispatcherPriority.Render` — `Video.MediaPlayer = vm.Player`, `ShowControls()`, `vm.Start()`.
- `OnLoaded`: аналогично, `UpdateCover()` до `Render`, потом привязка плеера.
- `UpdateChromeVisibility`: скрывает панель/стрелки/инфо, пока `IsBuffering`.
- `HideControlsIfPlaying` / `OnPauseShowTimerTick` учитывают `IsBuffering`.

### 5.3. `src/Prosmotr/ViewModels/VideoViewerViewModel.cs`

- `IsBuffering` — observable property.
- `BeginPlayback` / `Replay` ставят `IsBuffering = true` и вызывают `LoadAndPlayDeferred`.
- `SwitchTo` ставит `IsBuffering = true` **до** `StopAndRelease()`, чтобы cover отрисовался раньше очистки HWND.
- `OnPlaying` **не** сбрасывает `IsBuffering` сразу, а запускает `DispatcherTimer` на 400 мс.
- `_loadGen` guard'ит устаревшие отложенные загрузки.

### 5.4. `src/Prosmotr/Services/VideoPlaybackService.cs`

- `Load` упрощён: создаёт `Media`, опционально `:start-time`, присваивает `Player.Media`, освобождает старую `Media`.
- `:start-paused` убран.

---

## 6. Воспроизведение и проверка

### 6.1. Команды после любых правок

```powershell
# 1. Закрыть все процессы
Get-Process -Name "Prosmotr" -ErrorAction SilentlyContinue | Stop-Process -Force

# 2. Очистить кэш
Remove-Item -Path "app" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "src\Prosmotr\bin","src\Prosmotr\obj" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "tests\Prosmotr.Tests\bin","tests\Prosmotr.Tests\obj" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "$env:TEMP\Prosmotr*" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "$env:TEMP\.NET*" -Recurse -Force -ErrorAction SilentlyContinue

# 3. Переопубликовать
 dotnet publish src\Prosmotr\Prosmotr.csproj -c Release -o app
```

После публикации можно смотреть лог:
```powershell
Get-Content "$env:LOCALAPPDATA\Prosmotr\app.log" -Tail 30
```

### 6.2. Тестовый запуск с видео

```powershell
$video = "D:\путь\к\файлу.mp4"
& "$env:USERPROFILE\Desktop\Просмотр\app\Prosmotr.exe" "$video"
```

---

## 7. Открытые вопросы

1. На каком именно этапе виден белый квадрат?
   - В момент смены `MediaPlayer.Media`?
   - Во время `Play()` до `OnPlaying`?
   - После `OnPlaying` на один кадр?
2. Виден ли белый квадрат, если панель управления скрыта (`AutoHideControls = true`) до переключения?
3. Виден ли белый квадрат при переходе фото→видео или только видео→видео?
4. Зависит ли от темы? (пользователь упомянул тёмную тему)
5. Помогает ли увеличение задержки в `LoadAndPlayDeferred` (например, 50-100 мс) или `DispatcherPriority.Render`?

### 2.8. Убрано `:start-paused`, cover снимается по `TimeChanged > 0`

Анализ лога (03:43) показал:
- `OnPlaying` приходит через ~50–80 мс после `Load`/`Play` — слишком рано, раньше реального кадра.
- `OnPlaying` приходит дважды, вероятно из-за `:start-paused` + `Resume()`.
- Cover убирается по `OnPlaying`, но белый фон HWND всё ещё виден, потому что первый кадр ещё не отрисован.

Новая стратегия (17.06, следующая итерация):
- Убрана опция `:start-paused` из `VideoPlaybackService.Load`.
- Убран обработчик `OnPlaying` в `VideoPlaybackService` (он больше не снимает паузу).
- `IsBuffering` сбрасывается НЕ в `OnPlaying`, а в `OnTimeChanged` при `Time > 0`.
- Добавлен флаг `_firstFrameRendered`, чтобы cover убирался только один раз, по факту первого кадра.
- Логирование `[Flicker]` дополнено информацией о `firstFrameRendered` и `TimeChanged`.

**Реальный результат:** `TimeChanged` при resume-старте пришло сразу с `time=19016`, то есть LibVLC сообщил время раньше, чем отрисовал кадр. Cover убрался мгновенно, и белый квадрат остался.

### 2.9. Cover убирается по таймеру 250 мс после OnPlaying

Следующая итерация:
- Убрана логика `firstFrameRendered`.
- В `OnPlaying` запускается `DispatcherTimer` на 250 мс.
- По истечении таймера сбрасывается `IsBuffering`.
- Это даёт LibVLC фиксированное окно на отрисовку первого кадра, независимо от `TimeChanged`.

**Ожидаемый результат:** cover держится минимум 250 мс после старта, что должно перекрыть белый фон HWND. Возможен небольшой чёрный экран в начале.

### 2.10. Скрипт `publish.ps1`

Добавлен `./publish.ps1`, который делает все шаги очистки и публикации одной командой.

### 2.11. Cover до StopAndRelease и таймер 400 мс

Очередная итерация (17.06):
- В `SwitchTo` `IsBuffering = true` ставится **до** `_playback.StopAndRelease()`. Раньше cover поднимался уже после остановки старой дорожки, и нативное HWND успевало мигнуть светлым фоном в промежутке.
- Таймер снятия cover увеличен с 250 мс до **400 мс**, чтобы у тяжёлых файлов / первого кадра high-res было больше времени на декодирование и отрисовку.
- Логирование `[Flicker]` сохранено.

**Ожидаемый результат:** устраняется вспышка в момент `video→video`, когда старый кадр убирается раньше, чем WPF отрисовал чёрный cover; 400 мс после `OnPlaying` даёт LibVLC запас на первый реальный кадр.

### 2.12. Cover при удалении видео в полноэкранном режиме

Симптом: в fullscreen при удалении видео (клавиша Delete / кнопка на тулбаре) весь экран на ~1 с становится белым.

Причина: `MainViewModel.Delete` для видео вызывает `videoVm.StopAndRelease()` перед `_nav.RemoveAt`. Остановка плеера очищает нативное HWND LibVLC, и его светлый фон становится виден на весь экран. Чёрный cover поднимался только позже, в `SwitchTo` следующего видео.

Исправление (17.06): в `Delete` перед `StopAndRelease()` устанавливается `videoVm.IsBuffering = true`. Cover в оверлее закрывает белый фон в промежутке между остановкой старого плеера и переключением на следующий файл.

### 2.13. Кэширование `plugins.dat` LibVLC

Симптом: после очистки `app\` через `publish.ps1` первый запуск открывает главное меню, и только через 5–10 с появляются видео/миниатюры (генерация `plugins.dat`).

Причина: LibVLC при отсутствии `plugins.dat` сканирует папку плагинов; скрипт `publish.ps1` удалял `app\` вместе с кэшем.

Исправление (17.06):
- `LibVlcProvider.Warmup` копирует `plugins.dat` из `%LOCALAPPDATA%\Prosmotr\plugins.dat` в `app\` при отсутствии, и сохраняет свежий кэш туда после генерации.
- `publish.ps1` делает резервную копию `plugins.dat` перед очисткой `app\` и восстанавливает после публикации.

---

## 8. Рекомендация следующего шага

Если белые квадраты останутся, добавить логирование времени между `IsBuffering=true`, `StopAndRelease`, `Load`/`Play`, `OnPlaying` и снятием cover.  
По логам выбирать между дальнейшим увеличением задержки (600–800 мс) или подходом 4.2/4.3 (скрытие `VideoView` / двойной плеер).

