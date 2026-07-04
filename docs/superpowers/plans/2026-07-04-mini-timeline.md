# Мини-таймлайн видео — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Добавить тонкий неинтерактивный мини-таймлайн в нижнюю часть видео-оверлея, видимый только при скрытой панели управления, с настройкой порога по длительности и возможностью отключения.

**Architecture:** Два новых поля в `AppSettings` управляют функцией. `VideoViewerViewModel` предоставляет вычисляемое свойство `CanShowMiniTimeline` (настройки + длительность). `VideoViewerView` управляет видимостью в code-behind, синхронно с `_controlsShown`, чтобы не смешивать chrome-state и настройки. Настройки применяются вживую через `SettingsChanged`.

**Tech Stack:** WPF, WPF-UI 4.3, CommunityToolkit.Mvvm 8.4, .NET 8, x64.

## Global Constraints

- Проект `src/Prosmotr/Prosmotr.csproj`, `net8.0-windows`, `PlatformTarget=x64`.
- Single-file publish ЗАПРЕЩЁН; после изменений публиковать в `app\` (`dotnet publish -c Release -o app`).
- Nullable и ImplicitUsings включены; комментарии — на русском, по делу.
- Не использовать `Application.Current as App` (Service Locator убран, P3).
- Все изменения UI должны учитывать airspace LibVLCSharp.WPF и переиспользование View через `DataContextChanged`.
- После реализации обновить `AGENTS.md`.

---

## File Structure

| File | Responsibility |
|------|-----------------|
| `src/Prosmotr/Models/AppSettings.cs` | Новые поля `ShowMiniTimeline`, `MiniTimelineThresholdMinutes` и атрибут `[Range]`. |
| `src/Prosmotr/Services/SettingsService.cs` | Валидация новых полей в `ValidateAndFix` (через reflection по `[Range]`). |
| `src/Prosmotr/ViewModels/SettingsViewModel.cs` | Два новых bindable-свойства, обработчики изменений, загрузка/сохранение значений. |
| `src/Prosmotr/Views/SettingsWindow.xaml` | Две новые карточки в разделе «Видео». |
| `src/Prosmotr/ViewModels/VideoViewerViewModel.cs` | Свойство `CanShowMiniTimeline`, подписка на `SettingsChanged`. |
| `src/Prosmotr/Views/VideoViewerView.xaml` | Новый `ProgressBar` мини-таймлайна в оверлее. |
| `src/Prosmotr/Views/VideoViewerView.xaml.cs` | Управление видимостью мини-таймлайна в `UpdateChromeVisibility` и property-changed. |
| `tests/Prosmotr.Tests/SettingsValidationTests.cs` | Тесты валидации порога и значения по умолчанию. |
| `AGENTS.md` | Обновление подводных камней и раздела ручного тестирования. |

---

### Task 1: Настройки модели и валидация

**Files:**

- Modify: `src/Prosmotr/Models/AppSettings.cs`
- Modify: `src/Prosmotr/Services/SettingsService.cs`
- Test: `tests/Prosmotr.Tests/SettingsValidationTests.cs`

**Interfaces:**

- Consumes: существующий `AppSettings` и `SettingsService.ValidateAndFix`.
- Produces: `AppSettings.ShowMiniTimeline : bool = true`, `AppSettings.MiniTimelineThresholdMinutes : int [1,120] = 20`.

- [ ] **Step 1: Добавить поля в `AppSettings.cs`**

В раздел «Видео» (после `ArrowKeysSeekVideo`) добавить:

```csharp
/// <summary>Показывать мини-таймлайн при скрытой панели управления видео.</summary>
public bool ShowMiniTimeline { get; set; } = true;

/// <summary>Видео короче этого порога (в минутах) показывает мини-таймлайн при скрытой панели.</summary>
[Range(1, 120)]
public int MiniTimelineThresholdMinutes { get; set; } = 20;
```

- [ ] **Step 2: Добавить/проверить валидацию в `SettingsService.ValidateAndFix`**

Убедиться, что `ValidateAndFix` обходит все свойства с `[Range]` через reflection и клампит значения к допустимому диапазону. Если `MiniTimelineThresholdMinutes` вне `[1,120]` — установить `20`. Если логика уже generic — дополнительных изменений не требуется.

```csharp
private static void ClampRangeProperties(AppSettings settings)
{
    foreach (var prop in typeof(AppSettings).GetProperties())
    {
        var range = (RangeAttribute?)Attribute.GetCustomAttribute(prop, typeof(RangeAttribute));
        if (range == null) continue;
        var value = prop.GetValue(settings);
        if (value is int intValue && range.Minimum is int min && range.Maximum is int max)
        {
            prop.SetValue(settings, Math.Clamp(intValue, min, max));
        }
        else if (value is float floatValue && range.Minimum is float fmin && range.Maximum is float fmax)
        {
            prop.SetValue(settings, Math.Clamp(floatValue, fmin, fmax));
        }
    }
}
```

Вызвать `ClampRangeProperties` внутри `ValidateAndFix`.

- [ ] **Step 3: Написать тесты валидации**

```csharp
[Fact]
public void ValidateAndFix_Clamps_MiniTimelineThresholdMinutes()
{
    var broken = new AppSettings { MiniTimelineThresholdMinutes = 200 };
    SettingsService.ValidateAndFix(broken);
    Assert.Equal(120, broken.MiniTimelineThresholdMinutes);
}

[Fact]
public void ValidateAndFix_Clamps_MiniTimelineThresholdMinutes_Low()
{
    var broken = new AppSettings { MiniTimelineThresholdMinutes = 0 };
    SettingsService.ValidateAndFix(broken);
    Assert.Equal(1, broken.MiniTimelineThresholdMinutes);
}

[Fact]
public void Default_MiniTimelineThresholdMinutes_Is_20()
{
    var settings = new AppSettings();
    Assert.Equal(20, settings.MiniTimelineThresholdMinutes);
    Assert.True(settings.ShowMiniTimeline);
}
```

- [ ] **Step 4: Запустить тесты**

```bash
dotnet test tests\Prosmotr.Tests\Prosmotr.Tests.csproj --filter "FullyQualifiedName~MiniTimeline"
```

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Prosmotr/Models/AppSettings.cs src/Prosmotr/Services/SettingsService.cs tests/Prosmotr.Tests/SettingsValidationTests.cs
git commit -m "feat(settings): add ShowMiniTimeline and MiniTimelineThresholdMinutes with validation"
```

---

### Task 2: UI настроек (SettingsViewModel + SettingsWindow)

**Files:**

- Modify: `src/Prosmotr/ViewModels/SettingsViewModel.cs`
- Modify: `src/Prosmotr/Views/SettingsWindow.xaml`

**Interfaces:**

- Consumes: `AppSettings.ShowMiniTimeline`, `AppSettings.MiniTimelineThresholdMinutes`.
- Produces: bindable свойства `ShowMiniTimeline`, `MiniTimelineThresholdMinutes`, обработчики `OnShowMiniTimelineChanged`, `OnMiniTimelineThresholdMinutesChanged`.

- [ ] **Step 1: Добавить поля в `SettingsViewModel.cs`**

В секцию объявления наблюдаемых полей (рядом с `SeekStepSeconds`) добавить:

```csharp
[ObservableProperty]
private bool _showMiniTimeline;

[ObservableProperty]
[Range(1, 120)]
private int _miniTimelineThresholdMinutes;
```

- [ ] **Step 2: Загрузка и сохранение значений**

В `LoadFromSettings()` добавить:

```csharp
ShowMiniTimeline = _settings.Current.ShowMiniTimeline;
MiniTimelineThresholdMinutes = _settings.Current.MiniTimelineThresholdMinutes;
```

В `Commit()` добавить в конец (перед вызовом `Save`):

```csharp
_settings.Current.ShowMiniTimeline = ShowMiniTimeline;
_settings.Current.MiniTimelineThresholdMinutes = Math.Clamp(MiniTimelineThresholdMinutes, 1, 120);
```

- [ ] **Step 3: Обработчики изменений**

Добавить partial-методы:

```csharp
partial void OnShowMiniTimelineChanged(bool value) => Commit(immediate: true);
partial void OnMiniTimelineThresholdMinutesChanged(int value) => Commit(immediate: true);
```

- [ ] **Step 4: Добавить карточки в `SettingsWindow.xaml`**

В раздел «Видео», после карточки «Перематывать видео стрелками», добавить:

```xml
<Border Style="{StaticResource Card}">
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*" />
            <ColumnDefinition Width="Auto" />
        </Grid.ColumnDefinitions>
        <StackPanel>
            <TextBlock Text="Мини-таймлайн при скрытой панели" />
            <TextBlock Text="Тонкий индикатор прогресса внизу экрана, когда панель управления скрыта"
                       Opacity="0.6" FontSize="12" TextWrapping="Wrap" />
        </StackPanel>
        <ui:ToggleSwitch Grid.Column="1" IsChecked="{Binding ShowMiniTimeline, Mode=TwoWay}" />
    </Grid>
</Border>

<Border Style="{StaticResource Card}">
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*" />
            <ColumnDefinition Width="Auto" />
        </Grid.ColumnDefinitions>
        <StackPanel VerticalAlignment="Center">
            <TextBlock Text="Показывать мини-таймлайн для видео до" />
            <TextBlock Text="{Binding MiniTimelineThresholdMinutes, StringFormat={}{0} мин.}"
                       Opacity="0.6" FontSize="12" />
        </StackPanel>
        <Slider Grid.Column="1" Width="160" VerticalAlignment="Center"
                Minimum="1" Maximum="120" TickFrequency="1" IsSnapToTickEnabled="True"
                IsEnabled="{Binding ShowMiniTimeline}"
                Value="{Binding MiniTimelineThresholdMinutes, Mode=TwoWay}" />
    </Grid>
</Border>
```

- [ ] **Step 5: Сборка**

```bash
dotnet build src\Prosmotr\Prosmotr.csproj -c Debug
```

Expected: BUILD SUCCESS

- [ ] **Step 6: Commit**

```bash
git add src/Prosmotr/ViewModels/SettingsViewModel.cs src/Prosmotr/Views/SettingsWindow.xaml
git commit -m "feat(settings-ui): add mini-timeline toggle and threshold controls"
```

---

### Task 3: VM-свойство CanShowMiniTimeline

**Files:**

- Modify: `src/Prosmotr/ViewModels/VideoViewerViewModel.cs`

**Interfaces:**

- Consumes: `AppSettings.ShowMiniTimeline`, `AppSettings.MiniTimelineThresholdMinutes`, `ISettingsService.SettingsChanged`, `LengthMs`.
- Produces: `[ObservableProperty] bool CanShowMiniTimeline` (или ручное `OnPropertyChanged`), обновляемое при изменении длительности и настроек.

- [ ] **Step 1: Добавить observable-свойство**

В классе `VideoViewerViewModel` добавить:

```csharp
[ObservableProperty]
private bool _canShowMiniTimeline;
```

- [ ] **Step 2: Добавить метод пересчёта**

```csharp
private void UpdateCanShowMiniTimeline()
{
    if (_disposed) return;
    var thresholdMs = _settings.Current.MiniTimelineThresholdMinutes * 60000L;
    CanShowMiniTimeline = _settings.Current.ShowMiniTimeline
                          && LengthMs > 0
                          && LengthMs < thresholdMs;
}
```

- [ ] **Step 3: Вызвать пересчёт при изменении длительности**

В `OnLengthChanged` добавить в конец:

```csharp
UpdateCanShowMiniTimeline();
```

- [ ] **Step 4: Подписаться на изменение настроек**

В конструкторе `VideoViewerViewModel` (после инициализации полей) добавить:

```csharp
_settings.SettingsChanged += (_, _) => OnUi(UpdateCanShowMiniTimeline);
```

Убедиться, что `Dispose` отписывается:

```csharp
_settings.SettingsChanged -= (_, _) => OnUi(UpdateCanShowMiniTimeline);
```

> **Важно:** lambda не отписывается. Сохранить делегат в поле:

```csharp
private readonly EventHandler _onSettingsChanged;

// в конструкторе:
_onSettingsChanged = (_, _) => OnUi(UpdateCanShowMiniTimeline);
_settings.SettingsChanged += _onSettingsChanged;

// в Dispose:
_settings.SettingsChanged -= _onSettingsChanged;
```

- [ ] **Step 5: Инициализация при старте**

В `BeginPlayback()` или `Start()` вызвать `UpdateCanShowMiniTimeline()` после того, как `LengthMs` станет известен.

- [ ] **Step 6: Сборка**

```bash
dotnet build src\Prosmotr\Prosmotr.csproj -c Debug
```

Expected: BUILD SUCCESS

- [ ] **Step 7: Commit**

```bash
git add src/Prosmotr/ViewModels/VideoViewerViewModel.cs
git commit -m "feat(video-vm): add CanShowMiniTimeline based on settings and duration"
```

---

### Task 4: XAML мини-таймлайна

**Files:**

- Modify: `src/Prosmotr/Views/VideoViewerView.xaml`

**Interfaces:**

- Consumes: `CanShowMiniTimeline` из `VideoViewerViewModel`.
- Produces: элемент `MiniTimeline` (`ProgressBar`) в оверлее, привязанный к `PositionMs`/`LengthMs`.

- [ ] **Step 1: Добавить ProgressBar в оверлей**

Внутри `<Grid x:Name="Overlay">`, в `Grid.Row="1"`, после `ControlBar` (чтобы он был выше по Z-order), добавить:

```xml
<!-- Мини-таймлайн: тонкий прогресс при скрытой панели управления.
     IsHitTestVisible=False, чтобы клики проходили на ClickArea (пауза/полный экран). -->
<ProgressBar x:Name="MiniTimeline"
               Grid.Row="1"
               VerticalAlignment="Bottom"
               HorizontalAlignment="Stretch"
               Height="4"
               Minimum="0"
               Maximum="{Binding LengthMs}"
               Value="{Binding PositionMs}"
               Background="#55000000"
               Foreground="White"
               IsHitTestVisible="False"
               Visibility="Collapsed" />
```

> Примечание: `Visibility` управляется из code-behind, поэтому XAML-значение `Collapsed` — стартовое.

- [ ] **Step 2: Проверить вложенность**

`MiniTimeline` должен лежать внутри `<vlc:VideoView><Grid x:Name="Overlay">…</Grid></vlc:VideoView>`, в том же `Grid.Row="1"`, что и `ControlBar`. Визуально он будет внизу экрана.

- [ ] **Step 3: Сборка**

```bash
dotnet build src\Prosmotr\Prosmotr.csproj -c Debug
```

Expected: BUILD SUCCESS

- [ ] **Step 4: Commit**

```bash
git add src/Prosmotr/Views/VideoViewerView.xaml
git commit -m "feat(video-ui): add MiniTimeline progress bar in video overlay"
```

---

### Task 5: Code-behind управление видимостью

**Files:**

- Modify: `src/Prosmotr/Views/VideoViewerView.xaml.cs`

**Interfaces:**

- Consumes: `_controlsShown`, `_vm.CanShowMiniTimeline`, `_vm.IsBuffering`, `_vm.IsEnded`.
- Produces: корректное переключение `MiniTimeline.Visibility` вместе с панелью управления.

- [ ] **Step 1: Добавить поле code-behind**

В начало класса `VideoViewerView` добавить:

```csharp
private ProgressBar? _miniTimeline;
```

- [ ] **Step 2: Инициализировать поле в `OnLoaded`**

В `OnLoaded` (после `ControlBar = …`) добавить:

```csharp
_miniTimeline = FindName("MiniTimeline") as ProgressBar;
```

- [ ] **Step 3: Обновить `UpdateChromeVisibility`**

В метод `UpdateChromeVisibility` добавить после установки `ControlBar.Visibility`:

```csharp
bool showMini = !_controlsShown
                && _vm?.IsBuffering != true
                && _vm?.IsEnded != true
                && _vm?.CanShowMiniTimeline == true;
if (_miniTimeline != null)
    _miniTimeline.Visibility = showMini ? Visibility.Visible : Visibility.Collapsed;
```

- [ ] **Step 4: Реагировать на изменение VM-свойств**

В `OnVmPropertyChanged` добавить обработку:

```csharp
case nameof(VideoViewerViewModel.CanShowMiniTimeline):
case nameof(VideoViewerViewModel.IsEnded):
    UpdateChromeVisibility();
    break;
```

`IsBuffering` уже должен обрабатываться (проверить и добавить, если отсутствует).

- [ ] **Step 5: Очистка в `OnUnloaded`**

В `OnUnloaded` / `Detach` сбросить `_miniTimeline = null`.

- [ ] **Step 6: Сборка и тесты**

```bash
dotnet build src\Prosmotr\Prosmotr.csproj -c Debug
dotnet test tests\Prosmotr.Tests\Prosmotr.Tests.csproj
```

Expected: BUILD SUCCESS, все тесты PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Prosmotr/Views/VideoViewerView.xaml.cs
git commit -m "feat(video-ui): wire MiniTimeline visibility to chrome state"
```

---

### Task 6: Обновление документации AGENTS.md

**Files:**

- Modify: `AGENTS.md`

**Interfaces:**

- Consumes: завершённая реализация мини-таймлайна.
- Produces: актуальный раздел 5 с подводным камнем и раздел 7 с ручными проверками.

- [ ] **Step 1: Добавить подводный камень в раздел 5**

В `5.12. Автоскрытие «плавающих» элементов…` добавить абзац:

```markdown
**Мини-таймлайн видео при скрытой панели.** В `VideoViewerView` появляется тонкий `ProgressBar`
(`MiniTimeline`), который виден, когда основная панель управления скрыта. Видимость управляется
из code-behind (`UpdateChromeVisibility`) вместе с `ControlBar`, чтобы не смешивать chrome-state
и настройки. Показывается только если настройка `ShowMiniTimeline` включена, длительность видео
известна и **строго меньше** `MiniTimelineThresholdMinutes` (по умолчанию 20 мин). На паузе панель
управления всегда видна, поэтому мини-полоска не появляется. Элемент имеет `IsHitTestVisible=False`,
чтобы клики по видео (пауза/полный экран) продолжали работать. Порог настраивается в окне настроек
и применяется вживую.
```

- [ ] **Step 2: Обновить раздел 7 (ручное тестирование UI)**

Добавить пункты:

```markdown
- **Мини-таймлайн:** открыть видео короче 20 мин, дождаться автоскрытия панели — внизу появляется
  тонкая полоска, заполняющаяся по мере воспроизведения. Двинуть мышь — панель появляется, полоска
  исчезает. Видео длиннее порога — полоски нет. Пауза — полоски нет. Изменение настроек применяется
  без перезапуска.
```

- [ ] **Step 3: Commit**

```bash
git add AGENTS.md
git commit -m "docs(agents): document mini-timeline behavior and testing"
```

---

### Task 7: Публикация и ручная проверка

**Files:**

- Публикация: `app/`

**Interfaces:**

- Consumes: собранный проект.
- Produces: рабочий `app/Prosmotr.exe` с новой функцией.

- [ ] **Step 1: Закрыть запущенные процессы и очистить кэш**

```powershell
Get-Process -Name "Prosmotr" -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item -Path "app" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "src\Prosmotr\bin","src\Prosmotr\obj" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "$env:TEMP\Prosmotr*" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "$env:TEMP\.NET*" -Recurse -Force -ErrorAction SilentlyContinue
```

- [ ] **Step 2: Опубликовать в `app\`**

```bash
dotnet publish src\Prosmotr\Prosmotr.csproj -c Release -o app
```

Expected: PUBLISH SUCCESS

- [ ] **Step 3: Запустить и проверить вручную**

```powershell
Start-Process "app\Prosmotr.exe"
```

Проверить:

1. Видео < 20 мин — при скрытой панели появляется мини-полоска.
2. Движение мыши — панель появляется, полоска исчезает.
3. Видео > 20 мин — полоски нет.
4. Отключить «Мини-таймлайн» в настройках — полоски нет.
5. Поменять порог на 30 мин — видео 25 мин показывает полоску.
6. Пауза — полоски нет.

- [ ] **Step 4: Финальный коммит (если требуется)**

Если в процессе публикации появились изменения в `app/` — они в `.gitignore`, коммит не нужен.

---

## Spec Coverage Check

| Требование Spec | Задача |
|-----------------|--------|
| Поля `ShowMiniTimeline`, `MiniTimelineThresholdMinutes` в `AppSettings` | Task 1 |
| `[Range(1,120)]` валидация | Task 1 |
| UI настроек в разделе «Видео» | Task 2 |
| `CanShowMiniTimeline` в `VideoViewerViewModel` | Task 3 |
| Мини-таймлайн в оверлее видео | Task 4 |
| Видимость синхронно с `_controlsShown`, неинтерактивность | Task 5 |
| Вживое применение настроек | Task 2, Task 3 |
| Пауза не показывает мини-полоску | Task 5 (панель видна на паузе) |
| Порог в минутах, `LengthMs` в мс | Task 3 |
| Обновление `AGENTS.md` | Task 6 |
| Ручная проверка + публикация в `app\` | Task 7 |
| Анимация отложена | отмечено в Spec, не входит в план |

## Placeholder Scan

- Нет `TBD`, `TODO`, «implement later».
- Все шаги содержат конкретный код и команды.
- Имена свойств и методов согласованы между задачами: `ShowMiniTimeline`, `MiniTimelineThresholdMinutes`, `CanShowMiniTimeline`, `MiniTimeline`.
- Типы согласованы: `bool`, `int`, `ProgressBar`, `long` для `LengthMs`, `PositionMs`.

## Type Consistency Check

- `AppSettings.MiniTimelineThresholdMinutes` — `int`, `[Range(1, 120)]`.
- `SettingsViewModel.MiniTimelineThresholdMinutes` — `int`, `[Range(1, 120)]`.
- `VideoViewerViewModel.CanShowMiniTimeline` — `bool`.
- `MiniTimeline` — `ProgressBar`, `Value`/`Maximum` — `double`, binding к `PositionMs`/`LengthMs` (`long`, implicit conversion to `double`).

---

## Execution Options

**Plan complete and saved to `docs/superpowers/plans/2026-07-04-mini-timeline.md`. Two execution options:**

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration.

**2. Inline Execution** — Execute tasks in this session using `executing-plans`, batch execution with checkpoints.

Which approach do you prefer?
