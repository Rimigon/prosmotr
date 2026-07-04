# Мини-таймлайн видео при скрытой панели управления

## Контекст

В приложении **Просмотр** панель управления видео (`ControlBar`) скрывается автоматически при воспроизведении (`AutoHideControls`) или вручную по клавише `ToggleChromeKey`. Когда панель скрыта, пользователь не видит прогресс воспроизведения. Нужно добавить тонкий неинтерактивный индикатор прогресса (мини-таймлайн), который появляется вместо панели управления.

## Цель

Добавить минималистичный прогресс-бар в нижней части окна видео, видимый только при скрытой панели управления, с настраиваемым порогом по длительности видео и возможностью полного отключения.

## Требования

### 1. Поведение мини-таймлайна

- Мини-таймлайн показывается **только** когда:
  - настройка `ShowMiniTimeline` включена;
  - `_controlsShown == false` (полная панель управления скрыта);
  - видео не находится в состоянии буферизации (`IsBuffering == false`);
  - длительность видео известна и **строго меньше** порога `MiniTimelineThresholdMinutes`;
  - видео не завершилось (`IsEnded == false`) — при окончании показывается кнопка «Повторить» и панель управления не скрывается.
- Мини-таймлайн **скрывается**, когда:
  - `_controlsShown == true` (полная панель управления видна);
  - `IsBuffering == true`;
  - `ShowMiniTimeline == false`;
  - `LengthMs <= 0`;
  - `LengthMs >= threshold * 60000L`.
- На **паузе** панель управления остаётся видимой (существующее поведение), поэтому мини-таймлайн не актуален и не показывается.
- Мини-таймлайн **неинтерактивен**: клики по нему проходят на нижележащий `ClickArea` (пауза/полный экран).

### 2. Внешний вид

- Тонкий горизонтальный бар в самом низу оверлея видео, на всю ширину окна.
- Высота ≈ 3–4 DIP.
- Цвет заполнения — акцентный/белый (`White` или `Fluent` accent), фон — полупрозрачный тёмный.
- Без скруглений, без теней, без текста/иконок.
- Возможно небольшое свечение/тень внизу для контраста на тёмном видео (опционально, решается в верстке).

### 3. Настройки

Добавить в `AppSettings` (раздел «Видео»):

```csharp
/// <summary>Показывать мини-таймлайн при скрытой панели управления видео.</summary>
public bool ShowMiniTimeline { get; set; } = true;

/// <summary>Видео короче этого порога (в минутах) показывает мини-таймлайн при скрытой панели.</summary>
[Range(1, 120)]
public int MiniTimelineThresholdMinutes { get; set; } = 20;
```

### 4. UI настроек

В `SettingsWindow.xaml`, в разделе «Видео», добавить две карточки:

1. **«Мини-таймлайн при скрытой панели»**
   - `ToggleSwitch` привязан к `ShowMiniTimeline`.
2. **«Показывать для видео длительностью до»**
   - `Slider` `Minimum="1" Maximum="120" TickFrequency="1" IsSnapToTickEnabled="True"`.
   - Подпись `{X} мин.`
   - Видимость/активность зависит от `ShowMiniTimeline` (через `IsEnabled` или `Opacity`).

### 5. VM и View

#### 5.1. `VideoViewerViewModel`

- Добавить вычисляемое свойство `CanShowMiniTimeline`, которое объединяет:
  - `Settings.ShowMiniTimeline`;
  - `LengthMs > 0 && LengthMs < thresholdMs`.
  - Условие `_controlsShown` не входит в `CanShowMiniTimeline`: видимостью в нужный момент управляет code-behind `VideoViewerView`, чтобы не смешивать chrome-state VM и настройки.
- Обновлять это свойство при изменении:
  - `LengthMs`;
  - настроек (подписка на `ISettingsService.SettingsChanged`).

#### 5.2. `VideoViewerView.xaml`

- Добавить `ProgressBar` (или кастомный `Border` с `Rectangle` заполнения) внутрь `Overlay`:
  - расположение: `Grid.Row="1"`, `VerticalAlignment="Bottom"`, `HorizontalAlignment="Stretch"`;
  - `Value = {Binding PositionMs}`;
  - `Maximum = {Binding LengthMs}`;
  - `Visibility = {Binding ShowMiniTimeline, Converter={StaticResource BoolToVis}}` (либо управление из code-behind);
  - `IsHitTestVisible="False"` — чтобы не перехватывать клики;
  - поверх `ControlBar` по Z-order (позже в XAML) или в том же Grid.Row.
- Важно: мини-полоска должна скрываться, когда панель управления видна, и наоборот. Для этого лучше управлять видимостью из `UpdateChromeVisibility` code-behind, а не чистым binding'ом.

#### 5.3. `VideoViewerView.xaml.cs`

- Добавить поле `_miniTimeline` и связать с XAML.
- Обновить `UpdateChromeVisibility`:

```csharp
bool showMini = _controlsShown == false
                && _vm?.IsBuffering != true
                && _vm?.IsEnded != true
                && _vm?.CanShowMiniTimeline == true;
MiniTimeline.Visibility = showMini ? Visibility.Visible : Visibility.Collapsed;
```

- В `ShowControls()` и `HideControlsIfPlaying()` вызов `UpdateChromeVisibility` уже обеспечит переключение.
- При `OnVmPropertyChanged` для `CanShowMiniTimeline`, `LengthMs`, `IsBuffering`, `IsEnded` — вызывать `UpdateChromeVisibility`.

### 6. Живое применение настроек

- В `SettingsViewModel` добавить:
  - `OnShowMiniTimelineChanged(bool value) => Commit(immediate: true);`
  - `OnMiniTimelineThresholdMinutesChanged(int value) => Commit(immediate: true);`
- Эти настройки должны применяться вживую к открытому видео (как `Theme_` и `ShowThumbnails`).
- В `VideoViewerViewModel` подписаться на `ISettingsService.SettingsChanged` и пересчитывать `ShowMiniTimeline`.

### 7. Валидация и безопасность

- `MiniTimelineThresholdMinutes` клампится в `[1, 120]` при загрузке настроек и в `SettingsViewModel.Commit`.
- `SettingsService.ValidateAndFix` должен проверять `[Range]` атрибут.
- Сравнение длительности: `LengthMs < thresholdMinutes * 60000L` (threshold в миллисекундах).
- Если `LengthMs <= 0` (поток, неизвестная длительность) — мини-таймлайн не показывается.

### 8. Горячие клавиши

- `ToggleChromeKey` переключает `_controlsShown`, соответственно показывает/скрывает мини-таймлайн — без дополнительных изменений.
- Полноэкранный режим (`F`/`F11`) не влияет на логику; мини-таймлайн ведёт себя так же, как в окне.

### 9. Полноэкранный режим и airspace

- Мини-таймлайн остаётся внутри WPF-оверлея `LibVLCSharp.WPF`, как и `ControlBar`.
- Никаких изменений в Win32/fullscreen логике не требуется.

### 10. Тестирование

Ручные сценарии (юнит-тесты не покрывают UI/VLC):

1. Открыть видео 10 мин, дождаться автоскрытия панели — внизу появляется мини-таймлайн, заполняется по мере воспроизведения.
2. Двинуть мышь — панель управления появляется, мини-таймлайн исчезает.
3. Видео 25 мин при пороге 20 мин — мини-таймлайн не появляется.
4. Включить/выключить настройку «Мини-таймлайн» во время воспроизведения — изменение применяется сразу.
5. Поменять порог на 30 мин — видео 25 мин начинает показывать полоску.
6. Пауза — панель управления остаётся, мини-полоски нет.
7. Полноэкранный режим — поведение такое же, как в окне.
8. Буферизация (`IsBuffering == true`) — мини-полоска скрыта, пока не прогрузится первый кадр.

### 11. Границы и исключения

- Мини-таймлайн — **только для видео** (`VideoViewerView`), не для фото.
- Если пользователь отключил `AutoHideControls`, панель никогда не скрывается, и мини-таймлайн не появляется. Это ожидаемо.
- Если `ShowMiniTimeline == false`, UI настроек всё равно показывает слайдер, но неактивным (`IsEnabled="{Binding ShowMiniTimeline}"`).

### 12. Анимация (отложено)

Плавная анимация появления/скрытия мини-таймлайна вместо `Visibility` вынесена в отдельную задачу. Она требует:

- перехода от управления `Visibility` к `VisualStateManager`/`Storyboard`;
- учёта hit-test на время анимации;
- отдельного тестирования в полноэкранном/оконном режиме.

В рамках этого изменения анимация **не реализуется**.

---

## Связанные файлы

- `src/Prosmotr/Models/AppSettings.cs`
- `src/Prosmotr/ViewModels/SettingsViewModel.cs`
- `src/Prosmotr/ViewModels/VideoViewerViewModel.cs`
- `src/Prosmotr/Views/SettingsWindow.xaml`
- `src/Prosmotr/Views/VideoViewerView.xaml`
- `src/Prosmotr/Views/VideoViewerView.xaml.cs`
- `src/Prosmotr/Services/Abstractions/ISettingsService.cs` / `SettingsService.cs` (валидация)
- `tests/Prosmotr.Tests` (настройки / валидация, если применимо)

## Примечание

После реализации обновить `AGENTS.md` разделы 4 (структура), 5 (подводные камни/нюансы настроек и автоскрытия видео) и при необходимости раздел 7 (ручное тестирование UI).
