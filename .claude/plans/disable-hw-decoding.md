# План: убрать зелёный фон и лаги при первом запуске видео

## Проблема
При первом воспроизведении видео появляется зелёный фон и лаги. Это типичный симптом инициализации аппаратного декодирования DXVA/D3D11 в LibVLC: первый старт плеера с hardware decoding часто даёт артефакты, особенно в сочетании с `:start-time` (resume-позиция).

В текущем коде `LibVlcProvider` создаёт LibVLC без явного управления аппаратным декодированием, то есть оно включено по умолчанию.

## Решение
Добавить пользовательскую настройку «Аппаратное ускорение видео» (по умолчанию **выключено** для стабильности). Когда она выключена, LibVLC создаётся с флагом `--no-hw-dec`, что отключает всё аппаратное декодирование и устраняет артефакты/лаги первого старта.

## Изменения

### 1. `src/Prosmotr/Models/AppSettings.cs`
Добавить свойство в секцию «Видео»:
```csharp
/// <summary>Использовать аппаратное ускорение видео (DXVA/D3D11). По умолчанию отключено — стабильнее.</summary>
public bool UseHardwareDecoding { get; set; } = false;
```

### 2. `src/Prosmotr/Services/LibVlcProvider.cs`
- Изменить `LibVlc` getter и `Warmup` так, чтобы флаг `--no-hw-dec` добавлялся, когда `UseHardwareDecoding == false`.
- Поскольку `LibVlcProvider` — singleton и создаётся один раз, настройку нужно передать при создании. Варианты:
  - **(выбрано)** Сделать `LibVlcProvider` не через static-поле напрямую, а принимающим `ISettingsService` в конструкторе. Это чисто DI-способ и соответствует архитектуре проекта.
  - Сохранить static `_libVlc`, но читать настройку через статический флаг до первого создания LibVLC.

Предлагаемый подход: внедрить `ISettingsService` в `LibVlcProvider`, запомнить `_useHwDec` из `settings.Settings.UseHardwareDecoding`, и при создании LibVLC (оба места: getter и Warmup) добавлять массив опций:
```csharp
var options = new List<string>
{
    "--no-video-title-show",
    "--quiet",
    "--no-plugins-scan"
};
if (!_useHwDec)
    options.Add("--no-hw-dec");
_libVlc = new LibVLC(options.ToArray());
```

### 3. `src/Prosmotr/ViewModels/SettingsViewModel.cs`
- Добавить `[ObservableProperty] private bool _useHardwareDecoding;`
- Загрузить/сохранить в `LoadFromSettings` / `Commit`.
- Добавить `partial void OnUseHardwareDecodingChanged(bool value) => Commit();`

### 4. `src/Prosmotr/Views/SettingsWindow.xaml`
Добавить карточку в секцию «Видео» (например, после «Продолжать видео с сохранённой позиции»):
```xaml
<Border Style="{StaticResource Card}">
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*" />
            <ColumnDefinition Width="Auto" />
        </Grid.ColumnDefinitions>
        <StackPanel>
            <TextBlock Text="Аппаратное ускорение видео" />
            <TextBlock Text="DXVA/D3D11. Выключите, если появляется зелёный фон или лаги"
                       Opacity="0.6" FontSize="12" TextWrapping="Wrap" />
        </StackPanel>
        <ui:ToggleSwitch Grid.Column="1" IsChecked="{Binding UseHardwareDecoding, Mode=TwoWay}" />
    </Grid>
</Border>
```

## Побочные эффекты и компромиссы
- Отключение hardware decoding снижает производительность на слабых CPU при высоких разрешениях (4K/HEVC). Пользователь может включить обратно в настройках.
- Изменение настройки влётую не пересоздаёт уже работающий LibVLC — это приемлемо, потому что LibVLC создаётся один раз за сессию. При следующем запуске приложения новое значение применится. Можно добавить тост-уведомление «Изменение вступит в силу после перезапуска».

## Проверка
1. `dotnet test tests\Prosmotr.Tests\Prosmotr.Tests.csproj`
2. `dotnet publish src\Prosmotr\Prosmotr.csproj -c Release -o app`
3. Запустить `app\Prosmotr.exe`, открыть видео — зелёный фон и лаги первого старта должны исчезнуть (при выключенном ускорении по умолчанию).
4. Включить ускорение в настройках, перезапустить — behavior возвращается к hardware decoding (для сравнения).

## Порядок реализации
1. `AppSettings` → свойство.
2. `LibVlcProvider` → принимает `ISettingsService`, формирует опции.
3. `SettingsViewModel` → биндинг.
4. `SettingsWindow.xaml` → UI.
5. Сборка, тесты, публикация.
