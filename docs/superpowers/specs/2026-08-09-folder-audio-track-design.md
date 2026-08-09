# Озвучка на уровне папки (сезона сериала)

## Контекст

В приложении есть запоминание аудиодорожки **для конкретного файла** (`positions.json`,
`PlaybackPosition.AudioTrackId/AudioTrackName`, раздел 5.34 AGENTS.md): выбор озвучки в серии N
восстанавливается, только когда снова открывается именно эта серия. Для соседних серий той же
папки это не работает — каждая живёт со своей памятью (или без неё).

Пользователь смотрит сериалы по сезонам (папка = сезон) и постоянно переключает озвучку
вручную. Нужна **память на уровне папки**: выбор озвучки в одной серии становится дефолтом
папки и применяется ко всем её файлам при открытии — если такая дорожка у файла есть.

## Решения пользователя (уточнены)

1. **Область:** только текущая папка файла (сезон). Подпапки («Сезон 2» и т.п.) — своя память.
2. **Приоритет:** дефолт папки **важнее** пер-файловой памяти (папка — единый источник истины).
3. **«По умолчанию/Auto» (дорожка 0):** выбор Auto **сбрасывает** дефолт папки.

## Требования

### 1. Поведение

- Смена аудиодорожки (`SelectAudioTrack`, id > 0) в любой серии папки запоминается как дефолт папки.
- При открытии любого видео папки: если у папки есть дефолт — применяется он (по id, с фолбэком
  на имя дорожки); если дорожки в файле нет — остаётся дорожка по умолчанию.
- Выбор «По умолчанию» (id == 0) очищает дефолт папки.
- Настройка-тумблер **«Запоминать озвучку для папки (сериала)»** (по умолчанию вкл) отключает фичу
  целиком; записи при выключении не удаляются.
- Пер-файловая память продолжает работать для папок без дефолта (и для выключенной фичи).

### 2. Хранилище — `FolderAudioTrackStore` (новый файл, отдельно от positions.json)

`positions.json` — плоский `Dictionary<string, PlaybackPosition>`; встраивать туда папки = ломать
формат и тесты round-trip. Поэтому:

- `src/Prosmotr/Models/FolderAudioTrack.cs` — `{ int? AudioTrackId; string? AudioTrackName; }`.
- `src/Prosmotr/Services/Abstractions/IFolderAudioTrackStore.cs` —
  `Get(folderPath)` / `Set(folderPath, id, name)` / `Clear(folderPath)` / `Flush()`.
- `src/Prosmotr/Services/FolderAudioTrackStore.cs` — файл `%LOCALAPPDATA%\Prosmotr\folder-audio-tracks.json`,
  `Dictionary<string, FolderAudioTrack>`, ключ `ToLowerInvariant()` (регистр путей Windows),
  атомарная запись (tmp → `File.Move(overwrite)`), debounce 1.5 с, `Flush()` при выходе,
  переопределяемый `directory` для тестов. Полная копия паттерна `PlaybackPositionStore`.
- DI: `AddSingleton<IFolderAudioTrackStore, FolderAudioTrackStore>()`; `App.OnExit` дополнительно
  вызывает `Flush()` (рядом с `IPlaybackPositionStore`).

### 3. `VideoViewerViewModel`

- Конструктор принимает `IFolderAudioTrackStore` (фабрика в `App.ConfigureServices` обновляется).
- **`BeginPlayback`:** блок «Аудиодорожка» — приоритет: дефолт папки (`Item.DirectoryPath`,
  если `RememberAudioTrackPerFolder`) → пер-файловая память (`RememberAudioTrackPerFile`) → дефолт.
  Результат кладётся в существующие `_pendingAudioTrackId/_pendingAudioTrackName`;
  `OnPlaying` не меняется (`MatchAudioTrack` уже умеет id→имя и мягкий фолбэк).
- **`SelectAudioTrack`:** при id > 0 — `_folderTracks.Set(DirectoryPath, id, name)`; при id == 0 —
  `_folderTracks.Clear(DirectoryPath)`. Пер-файловая `SavePosition()` остаётся как есть.

### 4. Настройка

- `AppSettings.RememberAudioTrackPerFolder = true` (по умолчанию).
- `SettingsViewModel`: `[ObservableProperty]`, загрузка/сохранение в `Commit()`, `Commit()` на изменение.
- `SettingsWindow.xaml`: карточка-тумблер «Запоминать озвучку для папки (сериала)» после
  «Запоминать аудиодорожку для каждого файла».

## Крайние случаи

- Удаление файла: пер-файловая запись уходит (как сейчас), дефолт папки остаётся.
- Файл переехал в другую папку → применится дефолт новой папки.
- Папка с одним фильмом: дефолт создаётся, но невидим (один файл) — безвредно.
- PiP: тот же VM, выбор озвучки из PiP тоже обновляет папку — ок.
- Дорожка папки отсутствует в файле: `MatchAudioTrack` возвращает null → дорожка по умолчанию.

## Тесты

`tests/Prosmotr.Tests/FolderAudioTrackStoreTests.cs` (зеркало `PlaybackPositionStoreTests`):
round-trip, неизвестная папка → null, `Clear`, регистр ключей, персистентность между инстансами.

## Документация

`AGENTS.md` — новый раздел 5.35 с нюансами (папка главнее пер-файловой, Auto сбрасывает дефолт,
отдельный файл хранилища, почему не positions.json).
