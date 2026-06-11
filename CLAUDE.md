# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**Просмотр** — a WPF .NET 8 photo and video viewer for Windows 11, using MVVM architecture with Microsoft.Extensions.DependencyInjection. The UI follows Fluent Design (WPF-UI 4.3 with Mica), video playback uses LibVLCSharp, and image decoding uses Magick.NET for WEBP/HEIC. All source code and comments are in Russian.

> **For detailed descriptions of specific behaviors, edge cases, and gotchas, refer to `AGENTS.md`** in the repository root. `AGENTS.md` contains exhaustive notes that are too granular for this file.

## Build, Run, and Publish

From the repository root (`C:\Users\nikit\Desktop\Просмотр`):

```powershell
# Development build and run
dotnet build src\Prosmotr\Prosmotr.csproj -c Debug
dotnet run --project src\Prosmotr\Prosmotr.csproj

# Publish to app/ (required for desktop shortcut to see changes)
dotnet publish src\Prosmotr\Prosmotr.csproj -c Release -o app
```

**Important publishing constraints:**
- The desktop shortcut points to `app\Prosmotr.exe`, not `bin\`. After any code or XAML change, you must `dotnet publish … -o app` for the shortcut to reflect it. Close running instances first or files will be locked.
- **Do not use single-file publish** — it breaks LibVLC native plugin loading. Framework-dependent publish only.
- The project builds as **x64** (`<PlatformTarget>x64</PlatformTarget>`) because LibVLC native plugins live in `libvlc\win-x64\`.

There are **no test projects** in this repository.

## High-Level Architecture

### MVVM + DI Container

Entry point is `App.xaml.cs`. It builds an `IHost`, registers services and viewmodels in `ConfigureServices`, then shows `MainWindow`. All services are **singletons** (shared state for the app lifetime). Child VMs are created via **DI factories** registered as `Func<MediaItem, ImageViewerViewModel>` and `Func<MediaItem, VideoViewerViewModel>`.

### MainViewModel as Partial Class

`MainViewModel` is the central orchestrator and is split across five files using `partial class`:
- `MainViewModel.cs` — constructor, `UpdateCurrentContent`, `RefreshCommandStates`, DI wiring
- `MainViewModel.Gallery.cs` — opening files/folders, drag-and-drop, sorting logic
- `MainViewModel.Navigation.cs` — next/previous file navigation
- `MainViewModel.Presentation.cs` — fullscreen, slideshow
- `MainViewModel.Deletion.cs` — delete/restore/undo state
- `MainViewModel.FileActions.cs` — explorer, copy path, properties, open with

### Content Screen Routing

`MainWindow.xaml` contains a `ContentControl` bound to `MainViewModel.CurrentContent`. Implicit `DataTemplate`s map VM types to views:
- `EmptyStateViewModel` → `EmptyStateView`
- `ImageViewerViewModel` → `ImageViewerView`
- `VideoViewerViewModel` → `VideoViewerView`

The same `ImageViewerView` instance is **reused** when switching between photos (only `DataContext` changes); likewise, the same `VideoViewerViewModel` is **reused** when switching between videos via `SwitchTo()` to avoid player recreation.

### Key Services

All services live in `Services/` with interfaces in `Services/Abstractions/`:
- `ISettingsService` / `SettingsService` — `%APPDATA%\Prosmotr\settings.json`, debounced saves
- `INavigationService` / `NavigationService` — current file index and list
- `IMediaLibraryService` / `MediaLibraryService` — builds gallery from folder/file
- `IFileDeletionService` / `FileDeletionService` — moves to Recycle Bin via COM `IFileOperation` on an STA thread
- `IImageCache` / `ImageCache` — LRU cache of decoded full-size images (capacity 24, memory-capped)
- `IImageDecodingService` / `ImageDecodingService` — WPF for standard formats, Magick.NET → BMP for WEBP/HEIC
- `IThumbnailService` / `ThumbnailService` — thumbnail generation for the bottom strip
- `IPlaybackPositionStore` / `PlaybackPositionStore` — video resume positions in `%LOCALAPPDATA%\Prosmotr\positions.json`
- `IDisplayTopologyService` / `DisplayTopologyService` — Win32 CCD API for clone/extend display modes
- `INotificationService` / `NotificationService` — raises events consumed by `ToastView` in the UI layer

### Single-Instance Behavior

`App.xaml.cs` acquires a named mutex (`Prosmotr.SingleInstance.v1`). If another instance is running, the new process sends the file path via a named pipe (`Prosmotr.OpenFile.v1`) and exits. The running instance receives the path and opens it in the existing window.

### Notification System

`NotificationService` only raises a UI-thread event; the actual toast UI is rendered by `ToastView` controls. There is a `ToastView` inside `MainWindow` and another inside `VideoViewerView` (because LibVLC airspace would obscure the main one). `ToastView` queues up to 3 notifications and shows them sequentially.

### Hotkey Architecture

Because LibVLC's native video window (airspace) steals keyboard focus, `MainWindow` hooks hotkeys via **both** `PreviewKeyDown` and `ComponentDispatcher.ThreadPreprocessMessage` (Win32 `WM_KEYDOWN` thread-level interception). A `_suspendHotkeys` flag guards against hotkeys firing while modal dialogs are open.

## Important Cross-Cutting Concerns

- **STA-thread COM operations:** `FileDeletionService` and `RecycleBinRestore` execute COM APIs (`IFileOperation`, `Shell.Application`) on dedicated STA threads, not `Task.Run`. `FileDeletionService` uses `Task.WhenAny(tcs.Task, Task.Delay(10s))` to avoid permanently deadlocking its `SemaphoreSlim` if COM hangs.
- **Video airspace / overlay:** `LibVLCSharp.WPF` renders video in a separate native HWND. The WPF overlay (`VideoViewerView`) uses a Grid with background `#02000000` for hit-testing above the video. Because `ForegroundWindow` (the LibVLC overlay host) is a separate HWND, `RelativeSource AncestorType=Window` bindings inside `VideoViewerView` resolve to the wrong window — avoid them; use the cached `_mainVm` reference instead.
- **ZoomBorder image hosting:** `ImageViewerView.xaml` places the `Image` inside a `Canvas` (not a `Grid`) with `Stretch="None"`, so WPF does not apply layout-clip before `ZoomBorder`'s `RenderTransform`. Changing this back to `Grid` would clip large images.
- **Fullscreen implementation:** `WindowState.Maximized` is intentionally **not** used for fullscreen. Instead, `FullScreenHelper` removes `WindowChrome`, strips caption/thickframe styles via `SetWindowLongPtr`, and positions the window to the monitor bounds via `SetWindowPos`. A Win32 window subclass intercepts `WM_NCHITTEST` to disable resize borders. `DwmExtendFrameIntoClientArea` and `DWMWA_BORDER_COLOR` remove white DWM bars in Windows 11.
- **Sorting priority:** `MainViewModel.ResolveOrderingAsync` resolves gallery order in three steps: (1) per-folder manual sort stored in settings, (2) live Explorer sort order via `ExplorerSortReader`, (3) global `SortBy` setting.
- **Display topology (clone mode):** `DisplayTopologyService` calls Win32 `QueryDisplayConfig`/`SetDisplayConfig` (CCD API). This is a **system-wide** clone, not app-local. The app unconditionally restores extend mode on shutdown (`MainWindow.OnClosing` / `App.OnExit`). HRESULT checks use `hr != 0`, not `hr < 0`, because Win32 error codes are positive.

## Existing Documentation

For deep technical gotchas, always read **`AGENTS.md`** in the repository root. It contains exhaustive notes on:
- Critical behavior (single-instance, STA threads, COM timeouts, airspace focus)
- Performance optimizations (image cache, thumbnail batching, Magick → BMP)
- UI edge cases (ZoomBorder canvas, EmptyState scroll viewer, ContentControl reuse)
- Shutdown ordering (dispose VM before `LibVlcProvider`, flush positions before host dispose)
- Diagnostics via `%LOCALAPPDATA%\Prosmotr\app.log`

`AGENTS.md` is maintained in Russian and should be kept up to date alongside code changes.

**When you make changes to the project, update `AGENTS.md`** (and this file if architecture/commands change) so that future instances see accurate information. Do not leave outdated gotchas or command references in the documentation.
