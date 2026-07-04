# Thumbnail Scrollbar Improvements Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the thumbnail strip scrollbar thumb larger, scrolling smooth/pixel-based, and clicks on the scrollbar track jump directly to the clicked position.

**Architecture:** Replace `CanContentScroll="True"` with pixel scrolling in `ThumbnailStripView`, add a custom `ScrollBar` style with a minimum thumb size, and attach track-click handlers in code-behind that compute the target offset from the mouse position. Center the selected thumbnail in the visible area during navigation.

**Tech Stack:** WPF, C# 12, .NET 8, WPF-UI 4.3, CommunityToolkit.Mvvm.

## Global Constraints

- Target project: `src/Prosmotr/Prosmotr.csproj`.
- Platform target: `x64` (`PlatformTarget=x64`); do not change.
- Preserve existing MVVM patterns; code-behind is allowed for pure view interaction logic.
- Keep Russian comments in new code matching project convention (explain "why", not "what").
- Nullable reference types and implicit usings are enabled; do not introduce warnings.
- After code/XAML changes, publish to `app\` via `dotnet publish src\Prosmotr\Prosmotr.csproj -c Release -o app`.
- Unit tests must pass: `dotnet test tests\Prosmotr.Tests\Prosmotr.Tests.csproj`.
- Update `AGENTS.md` if new non-obvious behavior or gotchas are introduced.

---

## Task 1: Create a reusable helper to find child controls by type

**Files:**

- Create: `src/Prosmotr/Infrastructure/VisualTreeHelperExtensions.cs`

**Interfaces:**

- Produces: static method `public static T? FindChild<T>(this DependencyObject parent) where T : DependencyObject`
- Produces: static method `public static T? FindParent<T>(this DependencyObject child) where T : DependencyObject` (optional, only if needed)

- [ ] **Step 1: Create the helper file**

```csharp
using System.Windows;
using System.Windows.Media;

namespace Prosmotr.Infrastructure;

public static class VisualTreeHelperExtensions
{
    /// <summary>Находит первый визуальный потомок заданного типа.</summary>
    public static T? FindChild<T>(this DependencyObject parent) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed)
                return typed;

            var result = FindChild<T>(child);
            if (result != null)
                return result;
        }
        return null;
    }
}
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build src\Prosmotr\Prosmotr.csproj -c Debug`
Expected: 0 errors.

---

## Task 2: Add a custom ScrollBar style with a large thumb

**Files:**

- Modify: `src/Prosmotr/Views/ThumbnailStripView.xaml`

**Interfaces:**

- Consumes: existing `ListBox` named `List`.
- Produces: a `UserControl.Resources` block containing style `ThumbnailScrollBarStyle` for `ScrollBar`.

- [ ] **Step 1: Remove `CanContentScroll="True"`**

Edit the `ListBox` attributes:

```xml
<ListBox x:Name="List"
         ItemsSource="{Binding Items}"
         SelectedItem="{Binding Selected, Mode=TwoWay}"
         Background="Transparent" BorderThickness="0"
         ScrollViewer.HorizontalScrollBarVisibility="Auto"
         ScrollViewer.VerticalScrollBarVisibility="Disabled"
         VirtualizingPanel.IsVirtualizing="True"
         VirtualizingPanel.VirtualizationMode="Recycling"
         HorizontalContentAlignment="Stretch">
```

Remove `ScrollViewer.CanContentScroll="True"` entirely (default is `False`, which enables pixel-based scrolling).

- [ ] **Step 2: Add UserControl.Resources with the ScrollBar style**

Insert before the `ListBox`:

```xml
    <UserControl.Resources>
        <Style x:Key="ThumbnailScrollBarStyle" TargetType="ScrollBar">
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="ScrollBar">
                        <Grid>
                            <Border Background="{TemplateBinding Background}"/>
                            <Track x:Name="PART_Track" IsDirectionReversed="False">
                                <Track.DecreaseRepeatButton>
                                    <RepeatButton Command="ScrollBar.PageLeftCommand"
                                                  Style="{StaticResource {x:Static GridView.GridViewScrollViewerStyleKey}}"
                                                  Opacity="0"/>
                                </Track.DecreaseRepeatButton>
                                <Track.Thumb>
                                    <Thumb MinWidth="48" MinHeight="48"
                                           Background="{DynamicResource ControlFillColorDefaultBrush}"
                                           BorderBrush="{DynamicResource ControlStrokeColorDefaultBrush}"
                                           BorderThickness="1" CornerRadius="4"/>
                                </Track.Thumb>
                                <Track.IncreaseRepeatButton>
                                    <RepeatButton Command="ScrollBar.PageRightCommand"
                                                  Style="{StaticResource {x:Static GridView.GridViewScrollViewerStyleKey}}"
                                                  Opacity="0"/>
                                </Track.IncreaseRepeatButton>
                            </Track>
                        </Grid>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
    </UserControl.Resources>
```

Note: this template is intentionally minimal; it will be replaced/revised in Task 3 to support orientation and track-click handling. The important part now is `Thumb MinWidth="48" MinHeight="48"`.

- [ ] **Step 3: Verify XAML compiles**

Run: `dotnet build src\Prosmotr\Prosmotr.csproj -c Debug`
Expected: 0 errors. Warnings about the template are acceptable at this intermediate step.

---

## Task 3: Apply the style to the ListBox ScrollViewer and handle orientation

**Files:**

- Modify: `src/Prosmotr/Views/ThumbnailStripView.xaml`
- Modify: `src/Prosmotr/Views/ThumbnailStripView.xaml.cs`

**Interfaces:**

- Consumes: `VisualTreeHelperExtensions.FindChild<T>` from Task 1.
- Produces: method `private ScrollBar? GetScrollbar(ScrollViewer sv)` in code-behind.

- [ ] **Step 1: Update the ScrollBar style to support both orientations**

Replace the style from Task 2 with a more complete version:

```xml
    <UserControl.Resources>
        <Style x:Key="ThumbnailScrollBarStyle" TargetType="ScrollBar">
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="ScrollBar">
                        <Grid SnapsToDevicePixels="True">
                            <Border Background="{TemplateBinding Background}"
                                    CornerRadius="4"/>
                            <Track x:Name="PART_Track">
                                <Track.DecreaseRepeatButton>
                                    <RepeatButton Command="ScrollBar.PageLeftCommand"
                                                  Opacity="0" Background="Transparent"
                                                  Focusable="False"/>
                                </Track.DecreaseRepeatButton>
                                <Track.Thumb>
                                    <Thumb MinWidth="48" MinHeight="48"
                                           Background="{DynamicResource ControlFillColorDefaultBrush}"
                                           BorderBrush="{DynamicResource ControlStrokeColorDefaultBrush}"
                                           BorderThickness="1" CornerRadius="4"/>
                                </Track.Thumb>
                                <Track.IncreaseRepeatButton>
                                    <RepeatButton Command="ScrollBar.PageRightCommand"
                                                  Opacity="0" Background="Transparent"
                                                  Focusable="False"/>
                                </Track.IncreaseRepeatButton>
                            </Track>
                        </Grid>
                        <ControlTemplate.Triggers>
                            <Trigger Property="Orientation" Value="Vertical">
                                <Setter TargetName="PART_Track" Property="Orientation" Value="Vertical"/>
                                <Setter TargetName="PART_Track" Property="IsDirectionReversed" Value="True"/>
                            </Trigger>
                            <Trigger Property="Orientation" Value="Horizontal">
                                <Setter TargetName="PART_Track" Property="Orientation" Value="Horizontal"/>
                                <Setter TargetName="PART_Track" Property="IsDirectionReversed" Value="False"/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
    </UserControl.Resources>
```

- [ ] **Step 2: Attach the style to the ListBox ScrollViewer via attached property**

Add attached property after `HorizontalContentAlignment="Stretch"`:

```xml
         ScrollViewer.HorizontalScrollBarVisibility="Auto"
         ScrollViewer.VerticalScrollBarVisibility="Disabled"
         VirtualizingPanel.IsVirtualizing="True"
         VirtualizingPanel.VirtualizationMode="Recycling"
         HorizontalContentAlignment="Stretch">
```

WPF does not expose a direct attached property for `ScrollBar` style on `ScrollViewer`. We will apply the style in code-behind (Task 4) after the visual tree is loaded.

- [ ] **Step 3: Add Loaded handler and orientation change handler to apply styles**

In `ThumbnailStripView.xaml.cs`, add:

```csharp
private ScrollViewer? _scrollViewer;

public ThumbnailStripView()
{
    InitializeComponent();
    List.SelectionChanged += OnSelectionChanged;
    Loaded += OnLoaded;
    UpdateScrollBars();
}

private void OnLoaded(object sender, RoutedEventArgs e)
{
    ApplyScrollBarStyle();
}

private void ApplyScrollBarStyle()
{
    _scrollViewer = List.FindChild<ScrollViewer>();
    if (_scrollViewer == null) return;

    var style = (Style)FindResource("ThumbnailScrollBarStyle");
    var h = _scrollViewer.FindChild<ScrollBar>();
    var v = _scrollViewer.FindChild<ScrollBar>();
    // FindChild returns first match; we need both. Replace with named lookup via visual tree later if necessary.
}
```

This step intentionally leaves a partial implementation; the next task fully resolves scrollbar lookup.

- [ ] **Step 4: Build to catch syntax errors**

Run: `dotnet build src\Prosmotr\Prosmotr.csproj -c Debug`
Expected: 0 errors.

---

## Task 4: Find both horizontal and vertical scrollbars reliably

**Files:**

- Modify: `src/Prosmotr/Views/ThumbnailStripView.xaml.cs`
- Modify: `src/Prosmotr/Infrastructure/VisualTreeHelperExtensions.cs`

**Interfaces:**

- Consumes: `VisualTreeHelperExtensions.FindChild<T>`.
- Produces: `public static IEnumerable<T> FindChildren<T>(this DependencyObject parent)`.

- [ ] **Step 1: Add a children enumerator helper**

Add to `VisualTreeHelperExtensions`:

```csharp
public static IEnumerable<T> FindChildren<T>(this DependencyObject parent) where T : DependencyObject
{
    var count = VisualTreeHelper.GetChildrenCount(parent);
    for (int i = 0; i < count; i++)
    {
        var child = VisualTreeHelper.GetChild(parent, i);
        if (child is T typed)
            yield return typed;

        foreach (var descendant in FindChildren<T>(child))
            yield return descendant;
    }
}
```

- [ ] **Step 2: Find both scrollbars in the ScrollViewer**

Replace `ApplyScrollBarStyle` in `ThumbnailStripView.xaml.cs`:

```csharp
private ScrollBar? _horizontalScrollBar;
private ScrollBar? _verticalScrollBar;

private void ApplyScrollBarStyle()
{
    _scrollViewer = List.FindChild<ScrollViewer>();
    if (_scrollViewer == null) return;

    var style = (Style)FindResource("ThumbnailScrollBarStyle");
    var scrollbars = _scrollViewer.FindChildren<ScrollBar>().ToList();
    _horizontalScrollBar = scrollbars.FirstOrDefault(s => s.Orientation == Orientation.Horizontal);
    _verticalScrollBar = scrollbars.FirstOrDefault(s => s.Orientation == Orientation.Vertical);

    if (_horizontalScrollBar != null) _horizontalScrollBar.Style = style;
    if (_verticalScrollBar != null) _verticalScrollBar.Style = style;

    AttachTrackClickHandlers();
}
```

- [ ] **Step 3: Build**

Run: `dotnet build src\Prosmotr\Prosmotr.csproj -c Debug`
Expected: 0 errors.

---

## Task 5: Implement track-click jump-to-position for both orientations

**Files:**

- Modify: `src/Prosmotr/Views/ThumbnailStripView.xaml.cs`

**Interfaces:**

- Consumes: `_scrollViewer`, `_horizontalScrollBar`, `_verticalScrollBar`.
- Produces: `private void OnTrackMouseDown(object sender, MouseButtonEventArgs e)`.

- [ ] **Step 1: Add mouse-down handlers**

Append to `ThumbnailStripView.xaml.cs`:

```csharp
private void AttachTrackClickHandlers()
{
    if (_horizontalScrollBar != null)
        _horizontalScrollBar.PreviewMouseLeftButtonDown += OnHorizontalTrackMouseDown;
    if (_verticalScrollBar != null)
        _verticalScrollBar.PreviewMouseLeftButtonDown += OnVerticalTrackMouseDown;
}

private void OnHorizontalTrackMouseDown(object sender, MouseButtonEventArgs e)
{
    if (_scrollViewer == null || sender is not ScrollBar sb) return;
    var track = sb.FindChild<Track>();
    if (track == null || track.Thumb == null) return;

    var pos = e.GetPosition(track);
    var thumbWidth = track.Thumb.ActualWidth;
    var trackWidth = track.ActualWidth;

    // Ignore clicks on the thumb itself (standard drag should work)
    var thumbPos = track.Thumb.TranslatePoint(new Point(0, 0), track).X;
    if (pos.X >= thumbPos && pos.X <= thumbPos + thumbWidth)
        return;

    double usable = Math.Max(1, trackWidth - thumbWidth);
    double ratio = Math.Max(0, Math.Min(1, (pos.X - thumbWidth / 2) / usable));
    double target = ratio * _scrollViewer.ScrollableWidth;
    _scrollViewer.ScrollToHorizontalOffset(target);
    e.Handled = true;
}

private void OnVerticalTrackMouseDown(object sender, MouseButtonEventArgs e)
{
    if (_scrollViewer == null || sender is not ScrollBar sb) return;
    var track = sb.FindChild<Track>();
    if (track == null || track.Thumb == null) return;

    var pos = e.GetPosition(track);
    var thumbHeight = track.Thumb.ActualHeight;
    var trackHeight = track.ActualHeight;

    var thumbPos = track.Thumb.TranslatePoint(new Point(0, 0), track).Y;
    if (pos.Y >= thumbPos && pos.Y <= thumbPos + thumbHeight)
        return;

    double usable = Math.Max(1, trackHeight - thumbHeight);
    double ratio = Math.Max(0, Math.Min(1, (pos.Y - thumbHeight / 2) / usable));
    double target = ratio * _scrollViewer.ScrollableHeight;
    _scrollViewer.ScrollToVerticalOffset(target);
    e.Handled = true;
}
```

- [ ] **Step 2: Detach handlers on unload**

Add `Unloaded += OnUnloaded;` in constructor and implement:

```csharp
private void OnUnloaded(object sender, RoutedEventArgs e)
{
    if (_horizontalScrollBar != null)
        _horizontalScrollBar.PreviewMouseLeftButtonDown -= OnHorizontalTrackMouseDown;
    if (_verticalScrollBar != null)
        _verticalScrollBar.PreviewMouseLeftButtonDown -= OnVerticalTrackMouseDown;
}
```

- [ ] **Step 3: Build**

Run: `dotnet build src\Prosmotr\Prosmotr.csproj -c Debug`
Expected: 0 errors.

---

## Task 6: Center the selected thumbnail in the visible strip

**Files:**

- Modify: `src/Prosmotr/Views/ThumbnailStripView.xaml.cs`

**Interfaces:**

- Consumes: `List.SelectedItem`, `_scrollViewer`.
- Produces: `private void ScrollSelectedIntoCenter()`.

- [ ] **Step 1: Replace `OnSelectionChanged` logic**

Replace:

```csharp
private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
{
    if (List.SelectedItem != null)
        List.ScrollIntoView(List.SelectedItem);
}
```

with:

```csharp
private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
{
    if (List.SelectedItem != null)
        ScrollSelectedIntoCenter();
}

private void ScrollSelectedIntoCenter()
{
    if (_scrollViewer == null || List.SelectedItem == null) return;

    var container = List.ItemContainerGenerator.ContainerFromItem(List.SelectedItem) as FrameworkElement;
    if (container == null)
    {
        List.ScrollIntoView(List.SelectedItem);
        return;
    }

    if (Orientation == Orientation.Horizontal)
    {
        double itemCenter = container.TransformToAncestor(_scrollViewer).Transform(new Point(container.ActualWidth / 2, 0)).X;
        double viewportCenter = _scrollViewer.ViewportWidth / 2;
        double target = _scrollViewer.HorizontalOffset + itemCenter - viewportCenter;
        target = Math.Max(0, Math.Min(target, _scrollViewer.ScrollableWidth));
        _scrollViewer.ScrollToHorizontalOffset(target);
    }
    else
    {
        double itemCenter = container.TransformToAncestor(_scrollViewer).Transform(new Point(0, container.ActualHeight / 2)).Y;
        double viewportCenter = _scrollViewer.ViewportHeight / 2;
        double target = _scrollViewer.VerticalOffset + itemCenter - viewportCenter;
        target = Math.Max(0, Math.Min(target, _scrollViewer.ScrollableHeight));
        _scrollViewer.ScrollToVerticalOffset(target);
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src\Prosmotr\Prosmotr.csproj -c Debug`
Expected: 0 errors.

---

## Task 7: Re-apply style and handlers when orientation changes

**Files:**

- Modify: `src/Prosmotr/Views/ThumbnailStripView.xaml.cs`

**Interfaces:**

- Consumes: `OrientationProperty` change handler.
- Produces: updated `UpdateScrollBars` and orientation change flow.

- [ ] **Step 1: Update `UpdateScrollBars`**

Current:

```csharp
private void UpdateScrollBars()
{
    if (Orientation == Orientation.Horizontal)
    {
        ScrollViewer.SetHorizontalScrollBarVisibility(List, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(List, ScrollBarVisibility.Disabled);
    }
    else
    {
        ScrollViewer.SetHorizontalScrollBarVisibility(List, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(List, ScrollBarVisibility.Auto);
    }
}
```

Change to also re-apply style and handlers:

```csharp
private void UpdateScrollBars()
{
    if (Orientation == Orientation.Horizontal)
    {
        ScrollViewer.SetHorizontalScrollBarVisibility(List, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(List, ScrollBarVisibility.Disabled);
    }
    else
    {
        ScrollViewer.SetHorizontalScrollBarVisibility(List, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(List, ScrollBarVisibility.Auto);
    }

    // Style is already applied by name; re-discovery is needed after orientation change.
    ApplyScrollBarStyle();
}
```

Also update `OnOrientationChanged` to call `ApplyScrollBarStyle` after `UpdateScrollBars` if `IsLoaded`.

- [ ] **Step 2: Build**

Run: `dotnet build src\Prosmotr\Prosmotr.csproj -c Debug`
Expected: 0 errors.

---

## Task 8: Verify and run tests

**Files:**

- None modified.

- [ ] **Step 1: Run unit tests**

Run: `dotnet test tests\Prosmotr.Tests\Prosmotr.Tests.csproj`
Expected: all tests pass (currently 90 tests).

- [ ] **Step 2: Build release**

Run: `dotnet build src\Prosmotr\Prosmotr.csproj -c Release`
Expected: 0 errors, 0 warnings (new warnings are not acceptable).

---

## Task 9: Publish and manually verify

**Files:**

- None modified.

- [ ] **Step 1: Stop running instances**

```powershell
Get-Process -Name "Prosmotr" -ErrorAction SilentlyContinue | Stop-Process -Force
```

- [ ] **Step 2: Clean stale build output**

```powershell
Remove-Item -Path "src\Prosmotr\bin", "src\Prosmotr\obj" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "tests\Prosmotr.Tests\bin", "tests\Prosmotr.Tests\obj" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "$env:TEMP\Prosmotr*" -Recurse -Force -ErrorAction SilentlyContinue
```

- [ ] **Step 3: Publish to app folder**

```powershell
dotnet publish src\Prosmotr\Prosmotr.csproj -c Release -o app
```

- [ ] **Step 4: Launch and verify manually**

Launch: `app\Prosmotr.exe` (optionally with a folder containing many files as argument).

Checklist:

- [ ] Scrollbar thumb is at least 48 DIP wide/high.
- [ ] Scrolling with mouse wheel is smooth.
- [ ] Clicking anywhere on the empty scrollbar track jumps the strip to that position.
- [ ] Clicking and dragging the thumb still works.
- [ ] Selecting a file with arrow keys centers the thumbnail in the strip.
- [ ] No visual glitches or missing thumbnails during fast scrolling.

- [ ] **Step 5: Optional performance check on a folder >5000 files**

If a suitable folder is available, open it and check for stutter or high memory. If problematic, consider fallback to `CanContentScroll="True"` with fixed thumb size.

---

## Task 10: Update AGENTS.md if needed

**Files:**

- Modify: `AGENTS.md`

- [ ] **Step 1: Review AGENTS.md section 5**

If the thumbnail strip now behaves differently from what is documented (e.g., if AGENTS.md mentions `CanContentScroll`), update it. Otherwise, skip.

- [ ] **Step 2: Add a short note if a new gotcha appears**

For example, if pixel scrolling requires special care with virtualization, document it under section 5.

---

## Spec Coverage Check

| Spec Section | Task(s) Implementing It |
|---|---|
| Remove `CanContentScroll="True"` (pixel scroll) | Task 2 |
| Minimum thumb size 48 DIP | Task 2, Task 3 |
| Track click jumps to position | Task 4, Task 5 |
| Center selected thumbnail | Task 6 |
| Support both orientations | Task 3, Task 7 |
| Build and publish to `app\` | Task 8, Task 9 |
| Unit tests pass | Task 8 |
| Manual verification | Task 9 |
| AGENTS.md update | Task 10 |

---

## Placeholder Scan

No placeholders (TBD/TODO/"implement later"/"fill in") remain. Each step contains exact file paths, code, and expected outcomes.
