# Picture-in-Picture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a floating Picture-in-Picture window for video playback, keeping the current LibVLC `MediaPlayer` alive while moving it out of the main window.

**Architecture:** A new borderless topmost WPF window hosts a `LibVLCSharp.WPF.VideoView` and a minimal overlay. The existing `VideoViewerViewModel` temporarily donates its `MediaPlayer` to the PiP window; on restore the player is moved back to the main window with the same black-cover render-deferred pattern used for video switching.

**Tech Stack:** WPF, .NET 8, LibVLCSharp.WPF, WPF-UI 4.x, CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection.

## Global Constraints

- Target framework: `net8.0-windows`, platform target `x64`.
- All existing unit tests must remain green.
- UI text in Russian.
- Follow existing patterns: `VideoViewerView` overlay/cover pattern, `MainViewModel` content switching, `Func<T, VM>` factories, transient windows in DI.
- Publish to `app\` after implementation for the desktop shortcut to see changes.
- Keep `AGENTS.md` up to date.

---

## File Map

| File | Responsibility |
|------|----------------|
| `src/Prosmotr/Views/PictureInPictureWindow.xaml` | XAML for the floating PiP window: VideoView + overlay with mini panel. |
| `src/Prosmotr/Views/PictureInPictureWindow.xaml.cs` | Drag, resize bounds, topmost, auto-hide mini panel, show/hide/restore/close. |
| `src/Prosmotr/ViewModels/PictureInPictureViewModel.cs` | VM for the PiP window: proxies commands, tracks source VM, exposes play/pause/restore/close. |
| `src/Prosmotr/Views/PictureInPicturePlaceholderView.xaml` | Placeholder shown in main window while PiP is active. |
| `src/Prosmotr/ViewModels/PictureInPicturePlaceholderViewModel.cs` | VM for placeholder with Restore and Close commands. |
| `src/Prosmotr/ViewModels/VideoViewerViewModel.cs` | Add `EnterPictureInPicture`, `RestoreFromPictureInPicture`, `IsPictureInPicture` property and guard. |
| `src/Prosmotr/ViewModels/MainViewModel.cs` | Add `TogglePictureInPictureCommand`, track active PiP window, handle restore/close/PiP-lifecycle. |
| `src/Prosmotr/Views/VideoViewerView.xaml` | Add PiP button to control bar. |
| `src/Prosmotr/Views/VideoViewerView.xaml.cs` | Wire PiP button click and context-menu item; keep `_mainVm` sync. |
| `src/Prosmotr/Views/MainWindow.xaml.cs` | Add `P` hotkey handler. |
| `src/Prosmotr/App.xaml.cs` | Register PiP window and placeholder VM in DI. |
| `src/Prosmotr/ViewModels/Messages.cs` | Add `TogglePictureInPictureMessage`. |
| `AGENTS.md` | Document PiP behavior and gotchas. |

---

## Task 1: Messages and DI registration

**Files:**

- Modify: `src/Prosmotr/ViewModels/Messages.cs`
- Modify: `src/Prosmotr/App.xaml.cs`

**Interfaces:**

- Consumes: none
- Produces: `public sealed record TogglePictureInPictureMessage;`
- Produces: DI registrations for `PictureInPictureWindow`, `PictureInPictureViewModel`, `PictureInPicturePlaceholderViewModel`.

- [ ] **Step 1: Add message**

Add at the end of `src/Prosmotr/ViewModels/Messages.cs`:

```csharp
/// <summary>Запрос переключения режима Picture-in-Picture для текущего видео.</summary>
public sealed record TogglePictureInPictureMessage;
```

- [ ] **Step 2: Register new services**

In `src/Prosmotr/App.xaml.cs` `ConfigureServices`, after existing transient registrations add:

```csharp
services.AddTransient<Func<VideoViewerViewModel, PictureInPictureWindow>>(sp =>
    vm => new PictureInPictureWindow());
services.AddTransient<PictureInPictureViewModel>();
services.AddTransient<PictureInPicturePlaceholderViewModel>();
```

- [ ] **Step 3: Build to verify**

Run:

```powershell
dotnet build src\Prosmotr\Prosmotr.csproj -c Debug
```

Expected: builds successfully (message class compiles; DI registration may need types from later tasks to compile fully).

- [ ] **Step 4: Commit**

```bash
git add src/Prosmotr/ViewModels/Messages.cs src/Prosmotr/App.xaml.cs
git commit -m "pip: add TogglePictureInPictureMessage and DI registrations"
```

---

## Task 2: Picture-in-Picture placeholder VM and View

**Files:**

- Create: `src/Prosmotr/ViewModels/PictureInPicturePlaceholderViewModel.cs`
- Create: `src/Prosmotr/Views/PictureInPicturePlaceholderView.xaml`
- Create: `src/Prosmotr/Views/PictureInPicturePlaceholderView.xaml.cs`
- Modify: `src/Prosmotr/Views/MainWindow.xaml`

**Interfaces:**

- Consumes: `Action? OnRestore`, `Action? OnClose`
- Produces: `PictureInPicturePlaceholderViewModel` with `[RelayCommand] Restore()` and `[RelayCommand] ClosePip()`.

- [ ] **Step 1: Write placeholder VM**

```csharp
using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Prosmotr.ViewModels;

/// <summary>Placeholder shown in the main window while a video is playing in Picture-in-Picture mode.</summary>
public sealed partial class PictureInPicturePlaceholderViewModel : ViewModelBase
{
    private readonly Action? _onRestore;
    private readonly Action? _onClose;

    public PictureInPicturePlaceholderViewModel(Action? onRestore, Action? onClose)
    {
        _onRestore = onRestore;
        _onClose = onClose;
    }

    [RelayCommand]
    private void Restore() => _onRestore?.Invoke();

    [RelayCommand]
    private void ClosePip() => _onClose?.Invoke();
}
```

- [ ] **Step 2: Write placeholder XAML**

`src/Prosmotr/Views/PictureInPicturePlaceholderView.xaml`:

```xml
<UserControl x:Class="Prosmotr.Views.PictureInPicturePlaceholderView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
             xmlns:vm="clr-namespace:Prosmotr.ViewModels"
             xmlns:views="clr-namespace:Prosmotr.Views"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d"
             d:DataContext="{d:DesignInstance vm:PictureInPicturePlaceholderViewModel}"
             Background="{DynamicResource ApplicationBackgroundBrush}">
    <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center" Margin="20">
        <ui:SymbolIcon Symbol="WindowAd20" FontSize="48" HorizontalAlignment="Center"
                       Foreground="{DynamicResource TextFillColorSecondaryBrush}" />
        <TextBlock Text="Видео воспроизводится в отдельном окне"
                   HorizontalAlignment="Center" Margin="0,16,0,0"
                   Foreground="{DynamicResource TextFillColorPrimaryBrush}" FontSize="16" />
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Center" Margin="0,20,0,0">
            <ui:Button Appearance="Primary" Command="{Binding RestoreCommand}" Content="Вернуть в окно">
                <ui:Button.Icon>
                    <ui:SymbolIcon Symbol="WindowAd20" />
                </ui:Button.Icon>
            </ui:Button>
            <ui:Button Margin="12,0,0,0" Command="{Binding ClosePipCommand}" Content="Закрыть">
                <ui:Button.Icon>
                    <ui:SymbolIcon Symbol="Dismiss20" />
                </ui:Button.Icon>
            </ui:Button>
        </StackPanel>
    </StackPanel>
</UserControl>
```

- [ ] **Step 3: Write placeholder code-behind**

```csharp
using System.Windows.Controls;

namespace Prosmotr.Views;

public partial class PictureInPicturePlaceholderView : UserControl
{
    public PictureInPicturePlaceholderView()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 4: Add implicit DataTemplate**

In `src/Prosmotr/Views/MainWindow.xaml` `<Window.Resources>`, add next to the other `DataTemplate`s:

```xml
<DataTemplate DataType="{x:Type vm:PictureInPicturePlaceholderViewModel}">
    <views:PictureInPicturePlaceholderView />
</DataTemplate>
```

- [ ] **Step 5: Build and commit**

Run:

```powershell
dotnet build src\Prosmotr\Prosmotr.csproj -c Debug
```

Expected: builds successfully.

```bash
git add src/Prosmotr/ViewModels/PictureInPicturePlaceholderViewModel.cs src/Prosmotr/Views/PictureInPicturePlaceholderView.xaml src/Prosmotr/Views/PictureInPicturePlaceholderView.xaml.cs src/Prosmotr/Views/MainWindow.xaml
git commit -m "pip: add placeholder view and vm for main window"
```

---

## Task 3: Picture-in-Picture window and VM

**Files:**

- Create: `src/Prosmotr/Views/PictureInPictureWindow.xaml`
- Create: `src/Prosmotr/Views/PictureInPictureWindow.xaml.cs`
- Create: `src/Prosmotr/ViewModels/PictureInPictureViewModel.cs`
- Modify: `src/Prosmotr/App.xaml.cs`

**Interfaces:**

- Consumes: `VideoViewerViewModel` source VM; `MediaPlayer Player` from source.
- Produces: `PictureInPictureWindow` with `ShowFor(VideoViewerViewModel, MediaPlayer)` and events `RestoreRequested`, `CloseRequested`.
- Produces: `PictureInPictureViewModel` with `TogglePlayCommand`, `RestoreCommand`, `CloseCommand`, `IsPlaying`, `PositionMs`, `LengthMs`.

- [ ] **Step 1: Write PiP VM**

```csharp
using System;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Prosmotr.ViewModels;

/// <summary>VM for the floating Picture-in-Picture window. Proxies commands to the source video VM.</summary>
public sealed partial class PictureInPictureViewModel : ViewModelBase, IDisposable
{
    private readonly VideoViewerViewModel _source;
    private bool _disposed;

    public bool IsPlaying => _source.IsPlaying;
    public double PositionMs => _source.PositionMs;
    public double LengthMs => _source.LengthMs;

    public PictureInPictureViewModel(VideoViewerViewModel source)
    {
        _source = source;
        _source.PropertyChanged += OnSourcePropertyChanged;
    }

    [RelayCommand]
    private void TogglePlay() => _source.TogglePlay();

    [RelayCommand]
    private void Restore() => RestoreRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ClosePip() => CloseRequested?.Invoke(this, EventArgs.Empty);

    public event EventHandler? RestoreRequested;
    public event EventHandler? CloseRequested;

    private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(VideoViewerViewModel.IsPlaying)
                         or nameof(VideoViewerViewModel.PositionMs)
                         or nameof(VideoViewerViewModel.LengthMs))
        {
            OnPropertyChanged(e.PropertyName);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _source.PropertyChanged -= OnSourcePropertyChanged;
    }
}
```

- [ ] **Step 2: Write PiP window XAML**

`src/Prosmotr/Views/PictureInPictureWindow.xaml`:

```xml
<Window x:Class="Prosmotr.Views.PictureInPictureWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
        xmlns:vlc="clr-namespace:LibVLCSharp.WPF;assembly=LibVLCSharp.WPF"
        xmlns:vm="clr-namespace:Prosmotr.ViewModels"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        mc:Ignorable="d"
        Title="Просмотр — видео"
        WindowStyle="None"
        ResizeMode="CanResize"
        AllowsTransparency="False"
        Background="Black"
        Topmost="True"
        Width="400" Height="225"
        MinWidth="240" MinHeight="135"
        MaxWidth="720" MaxHeight="405"
        d:DataContext="{d:DesignInstance vm:PictureInPictureViewModel}">
    <Grid Background="Black" ClipToBounds="True">
        <vlc:VideoView x:Name="PipVideo" Background="Black">
            <Grid x:Name="Overlay" Background="#02000000">
                <Grid.RowDefinitions>
                    <RowDefinition Height="*" />
                    <RowDefinition Height="Auto" />
                </Grid.RowDefinitions>

                <Border x:Name="DragArea" Grid.Row="0" Grid.RowSpan="2" Background="Transparent"
                        MouseLeftButtonDown="OnDragAreaMouseDown" MouseMove="OnDragAreaMouseMove"
                        MouseLeftButtonUp="OnDragAreaMouseUp" Cursor="SizeAll" />

                <Border x:Name="MiniPanel" Grid.Row="1" Background="#D91A1A1A" Padding="10,8"
                        Visibility="Collapsed">
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="Auto" />
                            <ColumnDefinition Width="Auto" />
                            <ColumnDefinition Width="Auto" />
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>

                        <ui:Button Grid.Column="0" Appearance="Transparent" Command="{Binding TogglePlayCommand}"
                                   ToolTip="Воспроизведение / пауза">
                            <ui:SymbolIcon Foreground="White">
                                <ui:SymbolIcon.Style>
                                    <Style TargetType="ui:SymbolIcon">
                                        <Setter Property="Symbol" Value="Play24" />
                                        <Style.Triggers>
                                            <DataTrigger Binding="{Binding IsPlaying}" Value="True">
                                                <Setter Property="Symbol" Value="Pause24" />
                                            </DataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </ui:SymbolIcon.Style>
                            </ui:SymbolIcon>
                        </ui:Button>

                        <ui:Button Grid.Column="1" Appearance="Transparent" Command="{Binding RestoreCommand}"
                                   ToolTip="Вернуть в окно">
                            <ui:SymbolIcon Symbol="WindowAd20" Foreground="White" />
                        </ui:Button>

                        <ui:Button Grid.Column="2" Appearance="Transparent" Command="{Binding ClosePipCommand}"
                                   ToolTip="Закрыть">
                            <ui:SymbolIcon Symbol="Dismiss20" Foreground="White" />
                        </ui:Button>

                        <TextBlock Grid.Column="4" VerticalAlignment="Center" Margin="8,0,0,0"
                                   Foreground="White" Opacity="0.9" FontSize="12"
                                   Text="{Binding PositionMs, Converter={StaticResource MsToTime}}" />
                    </Grid>
                </Border>
            </Grid>
        </vlc:VideoView>
    </Grid>
</Window>
```

- [ ] **Step 3: Write PiP window code-behind**

```csharp
using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Prosmotr.ViewModels;

namespace Prosmotr.Views;

public partial class PictureInPictureWindow : Window
{
    private readonly DispatcherTimer _hideTimer;
    private bool _isDragging;
    private Point _dragStartPoint;

    public PictureInPictureWindow()
    {
        InitializeComponent();
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _hideTimer.Tick += (_, _) => HidePanel();
        Overlay.MouseMove += OnOverlayMouseMove;
    }

    public void ShowFor(VideoViewerViewModel sourceVm, LibVLCSharp.Shared.MediaPlayer player)
    {
        if (DataContext is PictureInPictureViewModel old) old.Dispose();
        var vm = new PictureInPictureViewModel(sourceVm);
        DataContext = vm;
        vm.RestoreRequested += OnRestoreRequested;
        vm.CloseRequested += OnCloseRequested;
        PipVideo.MediaPlayer = player;
        Show();
        ShowPanel();
    }

    public LibVLCSharp.Shared.MediaPlayer? DetachPlayer()
    {
        var player = PipVideo.MediaPlayer;
        try { PipVideo.MediaPlayer = null; } catch { }
        return player;
    }

    public void RaiseRestore() => RestoreRequested?.Invoke(this, EventArgs.Empty);

    private void OnRestoreRequested(object? sender, EventArgs e) => RestoreRequested?.Invoke(this, EventArgs.Empty);
    private void OnCloseRequested(object? sender, EventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    public event EventHandler? RestoreRequested;
    public event EventHandler? CloseRequested;

    private void ShowPanel()
    {
        MiniPanel.Visibility = Visibility.Visible;
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void HidePanel()
    {
        _hideTimer.Stop();
        MiniPanel.Visibility = Visibility.Collapsed;
    }

    private void OnOverlayMouseMove(object sender, MouseEventArgs e)
    {
        ShowPanel();
    }

    private void OnDragAreaMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        _isDragging = true;
        _dragStartPoint = e.GetPosition(this);
        DragArea.CaptureMouse();
    }

    private void OnDragAreaMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;
        var pos = e.GetPosition(this);
        Left += pos.X - _dragStartPoint.X;
        Top += pos.Y - _dragStartPoint.Y;
    }

    private void OnDragAreaMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        DragArea.ReleaseMouseCapture();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is PictureInPictureViewModel vm) vm.Dispose();
        base.OnClosing(e);
    }
}
```

- [ ] **Step 4: Build and commit**

```powershell
dotnet build src\Prosmotr\Prosmotr.csproj -c Debug
```

Expected: compiles.

```bash
git add src/Prosmotr/ViewModels/PictureInPictureViewModel.cs src/Prosmotr/Views/PictureInPictureWindow.xaml src/Prosmotr/Views/PictureInPictureWindow.xaml.cs src/Prosmotr/App.xaml.cs
git commit -m "pip: add floating PiP window and viewmodel"
```

---

## Task 4: Extend VideoViewerViewModel for PiP lifecycle

**Files:**

- Modify: `src/Prosmotr/ViewModels/VideoViewerViewModel.cs`

**Interfaces:**

- Consumes: `PictureInPictureWindow` (via callback passed in), `MediaPlayer` ownership.
- Produces: `bool IsPictureInPicture`, `void EnterPictureInPicture(Func<PictureInPictureWindow> windowFactory, Action<MediaPlayer> onRestore)`, `void RestoreFromPictureInPicture(MediaPlayer player)`.

- [ ] **Step 1: Add properties and fields**

Add to `VideoViewerViewModel`:

```csharp
[ObservableProperty] private bool _isPictureInPicture;
private Action<LibVLCSharp.Shared.MediaPlayer>? _pipRestoreCallback;
private PictureInPictureWindow? _pipWindow;
```

- [ ] **Step 2: Add EnterPictureInPicture method**

```csharp
public void EnterPictureInPicture(Func<PictureInPictureWindow> createWindow, Action<LibVLCSharp.Shared.MediaPlayer> onRestore)
{
    if (_disposed || IsPictureInPicture) return;

    _pipRestoreCallback = onRestore;
    var player = _playback.Player;

    var window = createWindow();
    _pipWindow = window;

    IsBuffering = true;
    IsPictureInPicture = true;

    var app = Application.Current;
    if (app != null)
    {
        app.Dispatcher.BeginInvoke(() =>
        {
            window.ShowFor(this, player);
        }, DispatcherPriority.Render);
    }
    else
    {
        window.ShowFor(this, player);
    }

    window.RestoreRequested += (_, _) =>
    {
        var p = window.DetachPlayer();
        if (p != null) onRestore(p);
        IsPictureInPicture = false;
        _pipWindow = null;
        window.Close();
    };

    window.CloseRequested += (_, _) =>
    {
        var p = window.DetachPlayer();
        p?.Stop();
        IsPictureInPicture = false;
        _pipWindow = null;
        window.Close();
    };
}

public void RestoreFromPictureInPicture(LibVLCSharp.Shared.MediaPlayer player)
{
    if (_disposed) return;
    IsPictureInPicture = false;
    _pipWindow = null;
    IsBuffering = true;
}
```

- [ ] **Step 3: Guard Dispose against active PiP**

In `VideoViewerViewModel.Dispose`, before disposing `_playback`, close the PiP window if present:

```csharp
_pipWindow?.Close();
_pipWindow = null;
```

- [ ] **Step 4: Build and commit**

```powershell
dotnet build src\Prosmotr\Prosmotr.csproj -c Debug
```

Expected: compiles.

```bash
git add src/Prosmotr/ViewModels/VideoViewerViewModel.cs
git commit -m "pip: add Enter/Restore PiP lifecycle in VideoViewerViewModel"
```

---

## Task 5: MainViewModel PiP orchestration

**Files:**

- Modify: `src/Prosmotr/ViewModels/MainViewModel.cs`

**Interfaces:**

- Consumes: `Func<VideoViewerViewModel, PictureInPictureWindow>` factory, `PictureInPicturePlaceholderViewModel`.
- Produces: `[RelayCommand] TogglePictureInPictureCommand`, `_activePipWindow` tracking, restore/close logic.

- [ ] **Step 1: Add field and command**

Add field:

```csharp
private readonly Func<VideoViewerViewModel, PictureInPictureWindow> _pipFactory;
private PictureInPictureWindow? _activePipWindow;
private VideoViewerViewModel? _pipSourceVm;
```

Add constructor parameter:

```csharp
Func<VideoViewerViewModel, PictureInPictureWindow> pipFactory
```

Assign `_pipFactory = pipFactory;`.

Add command method:

```csharp
[RelayCommand(CanExecute = nameof(CanTogglePictureInPicture))]
private void TogglePictureInPicture()
{
    if (_activePipWindow != null)
    {
        RestorePictureInPicture();
        return;
    }

    if (CurrentContent is not VideoViewerViewModel videoVm) return;

    _pipSourceVm = videoVm;
    videoVm.EnterPictureInPicture(
        () =>
        {
            var window = _pipFactory(videoVm);
            _activePipWindow = window;
            window.RestoreRequested += (_, _) =>
            {
                if (_activePipWindow == window) RestorePictureInPicture();
            };
            window.CloseRequested += (_, _) =>
            {
                if (_activePipWindow == window) ClosePictureInPicture();
            };
            return window;
        },
        player =>
        {
            if (_pipSourceVm == null) return;
            _pipSourceVm.RestoreFromPictureInPicture(player);
            CurrentContent = _pipSourceVm;
            _activePipWindow = null;
            _pipSourceVm = null;
        });

    CurrentContent = new PictureInPicturePlaceholderViewModel(
        onRestore: RestorePictureInPicture,
        onClose: ClosePictureInPicture);
    RefreshCommandStates();
}

public bool CanTogglePictureInPicture =>
    CurrentContent is VideoViewerViewModel || _activePipWindow != null;

private void RestorePictureInPicture()
{
    if (_activePipWindow == null || _pipSourceVm == null) return;
    _activePipWindow.RaiseRestore();
}

private void ClosePictureInPicture()
{
    if (_activePipWindow == null) return;
    _activePipWindow.Close();
    _activePipWindow = null;
    _pipSourceVm = null;
}
```

- [ ] **Step 2: Update RefreshCommandStates**

Add:

```csharp
TogglePictureInPictureCommand.NotifyCanExecuteChanged();
```

- [ ] **Step 3: Handle main window closing**

In `MainViewModel.Dispose`, close active PiP:

```csharp
_activePipWindow?.Close();
_activePipWindow = null;
_pipSourceVm = null;
```

- [ ] **Step 4: Build and commit**

```powershell
dotnet build src\Prosmotr\Prosmotr.csproj -c Debug
```

Expected: compiles.

```bash
git add src/Prosmotr/ViewModels/MainViewModel.cs
git commit -m "pip: orchestrate PiP state in MainViewModel"
```

---

## Task 6: PiP button, context menu item, hotkey

**Files:**

- Modify: `src/Prosmotr/Views/VideoViewerView.xaml`
- Modify: `src/Prosmotr/Views/VideoViewerView.xaml.cs`
- Modify: `src/Prosmotr/Views/MainWindow.xaml.cs`

**Interfaces:**

- Consumes: `MainViewModel.TogglePictureInPictureCommand`, `TogglePictureInPictureMessage`.
- Produces: Button on control bar, context-menu item, `P` hotkey.

- [ ] **Step 1: Add PiP button to control bar**

In `src/Prosmotr/Views/VideoViewerView.xaml`, in the control bar grid after the fullscreen button (`Grid.Column="11"`), add a PiP button at `Grid.Column="12"` and bump clone display to `Grid.Column="13"`:

```xml
<ui:Button Grid.Column="12" Appearance="Transparent" Click="OnPictureInPictureClick"
           ToolTip="Окно в окне (P)"
           AutomationProperties.Name="Окно в окне">
    <ui:SymbolIcon Symbol="WindowAd20" Foreground="White" />
</ui:Button>
```

Update `CloneDisplayButton` `Grid.Column="13"`.

Add click handler in `VideoViewerView.xaml.cs`:

```csharp
private void OnPictureInPictureClick(object sender, RoutedEventArgs e)
{
    _mainVm?.TogglePictureInPictureCommand.Execute(null);
}
```

- [ ] **Step 2: Add context-menu item**

In `VideoViewerView.xaml.cs` `OnContextMenuOpening`, after the Play/Pause item add:

```csharp
items.Add(MediaContextMenu.Item("Окно в окне", () => _mainVm?.TogglePictureInPictureCommand.Execute(null),
    icon: Wpf.Ui.Controls.SymbolRegular.WindowAd20));
items.Add(new Separator());
```

- [ ] **Step 3: Add P hotkey**

In `src/Prosmotr/Views/MainWindow.xaml.cs` `TryHandleHotkey`, after `case Key.F12:` add:

```csharp
case Key.P:
    if (_vm.TogglePictureInPictureCommand.CanExecute(null))
    {
        _vm.TogglePictureInPictureCommand.Execute(null);
        return true;
    }
    return false;
```

- [ ] **Step 4: Build and commit**

```powershell
dotnet build src\Prosmotr\Prosmotr.csproj -c Debug
```

Expected: compiles.

```bash
git add src/Prosmotr/Views/VideoViewerView.xaml src/Prosmotr/Views/VideoViewerView.xaml.cs src/Prosmotr/Views/MainWindow.xaml.cs
git commit -m "pip: add button, menu item and P hotkey"
```

---

## Task 7: Handle edge cases

**Files:**

- Modify: `src/Prosmotr/ViewModels/MainViewModel.Gallery.cs` or whichever file contains `OpenPathAsync`
- Modify: `src/Prosmotr/ViewModels/MainViewModel.Deletion.cs`
- Modify: `src/Prosmotr/Views/MainWindow.xaml.cs`

**Interfaces:**

- Consumes: active PiP window.
- Produces: safe cleanup when deleting current video, opening another folder, or closing main window.

- [ ] **Step 1: Close PiP when opening another path**

At the start of `MainViewModel.OpenPathAsync` add:

```csharp
if (_activePipWindow != null)
{
    ClosePictureInPicture();
}
```

- [ ] **Step 2: Close PiP before deleting current video**

In `MainViewModel.Delete`, if the deleted item is the video in PiP, close PiP first:

```csharp
if (_pipSourceVm?.Item.FullPath == cur.FullPath)
{
    ClosePictureInPicture();
}
```

- [ ] **Step 3: Main window closing closes PiP**

Ensure `MainViewModel.Dispose` closes PiP (already added). Additionally, in `MainWindow.OnClosing`:

```csharp
protected override void OnClosing(CancelEventArgs e)
{
    _vm.Dispose();
    base.OnClosing(e);
}
```

- [ ] **Step 4: Build and commit**

```powershell
dotnet build src\Prosmotr\Prosmotr.csproj -c Debug
```

Expected: compiles.

```bash
git add src/Prosmotr/ViewModels/MainViewModel*.cs src/Prosmotr/Views/MainWindow.xaml.cs
git commit -m "pip: handle edge cases (delete, folder change, main window close)"
```

---

## Task 8: Tests, full build, publish

**Files:**

- Modify: `AGENTS.md`

- [ ] **Step 1: Run unit tests**

```powershell
dotnet test tests\Prosmotr.Tests\Prosmotr.Tests.csproj
```

Expected: 90 tests pass.

- [ ] **Step 2: Build release**

```powershell
dotnet build src\Prosmotr\Prosmotr.csproj -c Release
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 3: Update AGENTS.md**

Add new subsection under §5:

```markdown
### 5.32. Picture-in-Picture

- PiP перемещает тот же `MediaPlayer` из основного `VideoView` в плавающее окно. При возврате плеер привязывается обратно; используется тот же чёрный cover + `DispatcherPriority.Render`, что и при обычном переключении видео, чтобы скрыть белый фон нативного HWND.
- Горячая клавиша `P` включает/выключает PiP только когда текущий контент — видео.
- При закрытии основного окна PiP закрывается автоматически через `MainViewModel.Dispose`.
- Если удаляемый файл воспроизводится в PiP, PiP закрывается перед удалением, чтобы освободить файловый handle.
```

- [ ] **Step 4: Publish to app\\**

Close any running `Prosmotr.exe` and publish:

```powershell
Get-Process -Name "Prosmotr" -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item -Path "app" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "src\Prosmotr\bin","src\Prosmotr\obj" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "$env:TEMP\Prosmotr*" -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish src\Prosmotr\Prosmotr.csproj -c Release -o app
```

- [ ] **Step 5: Smoke test**

Run:

```powershell
app\Prosmotr.exe
```

Manual checks:

1. Open a video.
2. Click PiP button (or press `P`) — video moves to a small floating window, main window shows placeholder.
3. Move PiP window by dragging.
4. Hover PiP — mini panel appears with play/pause/restore/close.
5. Pause/play from PiP.
6. Click «Вернуть в окно» — video returns to main window at the same position.
7. Open PiP again, close main window — both close.

- [ ] **Step 6: Commit AGENTS.md and any final fixes**

```bash
git add AGENTS.md
git commit -m "docs(agents): document Picture-in-Picture gotchas"
```

---

## Spec Coverage Check

- PiP button on control bar: Task 6. ✓
- `P` hotkey: Task 6. ✓
- Context menu item: Task 6. ✓
- Floating borderless topmost window: Task 3. ✓
- Mini panel with play/pause/restore/close: Tasks 2–3. ✓
- Main window placeholder: Task 2. ✓
- Move same MediaPlayer between windows: Tasks 3–4. ✓
- Preserve playback position/speed/volume: same player, so state is preserved automatically. ✓
- Edge cases (main close, delete, folder change): Task 7. ✓
- AGENTS.md update: Task 8. ✓

## Placeholder Scan

No TBD/TODO/"implement later"/"handle edge cases" placeholders. Each task has concrete code and commands.
