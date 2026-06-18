# План: полноэкранный режим на F11 с настраиваемой клавишей

## Цель
1. Сделать F11 стандартной клавишей переключения полноэкранного режима.
2. Добавить в настройки выпадающий список, чтобы пользователь мог выбрать другую клавишу для fullscreen.
3. Сохранить работу жёстко зашитой клавиши F как запасного варианта (обратная совместимость).

## Файлы для изменения

### 1. `src/Prosmotr/Models/AppSettings.cs`
- Добавить свойство:
  ```csharp
  /// <summary>Клавиша для переключения полноэкранного режима (название из System.Windows.Input.Key).</summary>
  public string FullScreenKey { get; set; } = "F11";
  ```

### 2. `src/Prosmotr/ViewModels/SettingsViewModel.cs`
- Заменить статический `ExitKeys` на приватный мастер-список `AllConfigurableKeys`.
- Сделать публичные `ExitKeyItems`, `ChromeKeyItems`, `FullScreenKeyItems` вычисляемыми списками, исключающими ключи, занятые другими настройками.
- Добавить `[ObservableProperty] private string _fullScreenKey = "F11";`.
- В `LoadFromSettings` загружать `FullScreenKey` и разрешать конфликты (если вручную испорчен JSON): при совпадении с `ExitKey`/`ToggleChromeKey` сбрасывать конфликтующую настройку к её дефолту.
- В `Commit` сохранять `FullScreenKey`.
- В `OnFullScreenKeyChanged`, `OnExitKeyChanged`, `OnToggleChromeKeyChanged` вызывать `Commit()` и поднимать `PropertyChanged` для всех трёх списков, чтобы выпадающие списки "не предлагали" уже занятые клавиши.

### 3. `src/Prosmotr/Views/SettingsWindow.xaml`
- Добавить карточку "Клавиша полноэкранного режима" с `ComboBox`, привязанным к `FullScreenKey` и `FullScreenKeyItems`.
- Обновить привязки существующих списков `ExitKey`/`ToggleChromeKey` на `ExitKeyItems`/`ChromeKeyItems`.

### 4. `src/Prosmotr/Views/MainWindow.xaml.cs`
- В `TryHandleHotkey`, перед `switch`, добавить проверку настроенной клавиши fullscreen:
  ```csharp
  if (Enum.TryParse<Key>(_settings.Settings.FullScreenKey, out var fsKey) && key == fsKey)
  {
      _vm.ToggleFullScreenCommand.Execute(null);
      return true;
  }
  ```
- Оставить `case Key.F` в `switch` как запасной вариант.

### 5. `src/Prosmotr/Views/MainWindow.xaml`
- Обновить tooltip кнопки fullscreen: `ToolTip="Полный экран (F11 / F)"`.

### 6. `AGENTS.md`
- В разделе 5.11 (горячие клавиши) добавить строку про настраиваемую клавишу fullscreen (по умолчанию F11, F — запасной вариант).

### 7. `tests/Prosmotr.Tests/SettingsValidationTests.cs` (опционально)
- Добавить тест, что некорректное/пустое `FullScreenKey` заменяется на дефолт при загрузке.

## Проверка
- `dotnet test tests\Prosmotr.Tests\Prosmotr.Tests.csproj`
- `dotnet publish src\Prosmotr\Prosmotr.csproj -c Release -o app`
- Запустить `app\Prosmotr.exe`, открыть фото/видео и проверить:
  - F11 переключает fullscreen;
  - F продолжает переключать fullscreen;
  - в настройках можно выбрать другую клавишу и она работает;
  - выпадающие списки не позволяют назначить одну и ту же клавишу на два действия.

## Риски / нюансы
- Airspace VLC: горячие клавиши обрабатываются в `TryHandleHotkey` через `ComponentDispatcher.ThreadPreprocessMessage`, поэтому добавление новой клавиши в ту же функцию будет работать и для видео.
- Список конфликтующих клавиш: F остаётся зашитой и по-прежнему исключается из настраиваемых списков; F11 добавляется в настраиваемые списки, но фильтрация по другим выбранным клавишам предотвращает двойное назначение.
- При загрузке битого/конфликтующего JSON — автоматический откат к дефолтам, как у `ExitKey`/`ToggleChromeKey`.
