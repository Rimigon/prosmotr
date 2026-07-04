# Folder Media Counters Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add per-folder counters for photos, GIFs, and videos and display them in both normal and fullscreen views.

**Architecture:** Add an observable `FolderSummaryText` property to `MainViewModel`, populate it from `_nav.Items` on every `ListChanged`, and bind it to a new row under the status bar and to the fullscreen info overlay.

**Tech Stack:** WPF, .NET 8, CommunityToolkit.Mvvm, WPF-UI 4.3.

## Global Constraints

- Follow existing code style and Russian UI copy.
- Keep existing `StatusText` formatting unchanged.
- Use `[ObservableProperty]` source generators from CommunityToolkit.Mvvm.
- Changes must compile under `net8.0-windows`, x64.
- After code/XAML changes, publish to `app\` with `dotnet publish src\Prosmotr\Prosmotr.csproj -c Release -o app`.
- No commits without explicit user approval.

---

## File map

- `src/Prosmotr/ViewModels/MainViewModel.cs` — add `FolderSummaryText` property and a private `UpdateFolderSummary()` method; call it from `OnListChanged()`.
- `src/Prosmotr/Views/MainWindow.xaml` — add a new `TextBlock` row under the center status text and adjust the fullscreen info overlay to include the summary.

---

### Task 1: Compute folder summary text in MainViewModel

**Files:**

- Modify: `src/Prosmotr/ViewModels/MainViewModel.cs`
- Test: `tests/Prosmotr.Tests/MainViewModelTests.cs` (create if missing)

**Interfaces:**

- Consumes: `_nav.Items` (`IReadOnlyList<MediaItem>`) and `MediaItem.MediaType`.
- Produces: `[ObservableProperty] private string _folderSummaryText = string.Empty;` and `private void UpdateFolderSummary()`.

- [ ] **Step 1: Write the failing test**

```csharp
using Prosmotr.Models;
using Prosmotr.ViewModels;
using Xunit;

namespace Prosmotr.Tests;

public class FolderSummaryTests
{
    [Theory]
    [InlineData(12, 3, 1, "12 фото, 3 видео, 1 GIF")]
    [InlineData(1, 0, 0, "1 фото")]
    [InlineData(0, 2, 0, "2 видео")]
    [InlineData(0, 0, 5, "5 GIF")]
    [InlineData(7, 1, 0, "7 фото, 1 видео")]
    public void BuildSummaryText_ReturnsExpected(int images, int videos, int gifs, string expected)
    {
        var actual = MainViewModel.BuildFolderSummaryText(images, videos, gifs);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void BuildSummaryText_AllZero_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, MainViewModel.BuildFolderSummaryText(0, 0, 0));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests\Prosmotr.Tests\Prosmotr.Tests.csproj --filter "FullyQualifiedName~FolderSummaryTests" -v n`
Expected: FAIL — `BuildFolderSummaryText` does not exist.

- [ ] **Step 3: Add property and helper method**

In `src/Prosmotr/ViewModels/MainViewModel.cs`, add:

1. Near the other `[ObservableProperty]` fields:

```csharp
[ObservableProperty] private string _folderSummaryText = string.Empty;
```

1. A public static helper:

```csharp
public static string BuildFolderSummaryText(int imageCount, int videoCount, int animatedCount)
{
    if (imageCount == 0 && videoCount == 0 && animatedCount == 0)
        return string.Empty;

    var parts = new List<string>(3);
    if (imageCount > 0) parts.Add($"{imageCount} фото");
    if (videoCount > 0) parts.Add($"{videoCount} видео");
    if (animatedCount > 0) parts.Add($"{animatedCount} GIF");

    return string.Join(", ", parts);
}
```

1. A private updater that consumes `_nav.Items`:

```csharp
private void UpdateFolderSummary()
{
    if (_nav.Items.Count == 0)
    {
        FolderSummaryText = string.Empty;
        return;
    }

    int images = 0, videos = 0, animated = 0;
    foreach (var item in _nav.Items)
    {
        switch (item.MediaType)
        {
            case MediaType.Image: images++; break;
            case MediaType.Video: videos++; break;
            case MediaType.AnimatedImage: animated++; break;
        }
    }

    FolderSummaryText = BuildFolderSummaryText(images, videos, animated);
}
```

- [ ] **Step 4: Call updater from OnListChanged**

Inside `OnListChanged()`, after `UpdateStatus();`, add:

```csharp
UpdateFolderSummary();
```

- [ ] **Step 5: Run tests**

Run: `dotnet test tests\Prosmotr.Tests\Prosmotr.Tests.csproj --filter "FullyQualifiedName~FolderSummaryTests" -v n`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Prosmotr/ViewModels/MainViewModel.cs tests/Prosmotr.Tests/MainViewModelTests.cs
git commit -m "feat: compute folder photo/video/GIF summary text"
```

---

### Task 2: Show summary under status bar

**Files:**

- Modify: `src/Prosmotr/Views/MainWindow.xaml:90-100`

**Interfaces:**

- Consumes: `MainViewModel.FolderSummaryText`.
- Produces: a visible `TextBlock` row in the bottom status area.

- [ ] **Step 1: Wrap status controls in a two-row panel**

Replace the single centered `TextBlock` in the bottom toolbar with a `StackPanel` containing two `TextBlock`s. Locate the existing block:

```xml
<!-- Центр: статус -->
<TextBlock Grid.Column="1" Text="{Binding StatusText}" Opacity="0.75"
           HorizontalAlignment="Center" VerticalAlignment="Center"
           TextTrimming="CharacterEllipsis" />
```

Change it to:

```xml
<!-- Центр: статус + сводка по папке -->
<StackPanel Grid.Column="1" Orientation="Vertical" HorizontalAlignment="Center" VerticalAlignment="Center">
    <TextBlock Text="{Binding StatusText}" Opacity="0.75"
               HorizontalAlignment="Center" VerticalAlignment="Center"
               TextTrimming="CharacterEllipsis" />
    <TextBlock Text="{Binding FolderSummaryText}" Opacity="0.55" FontSize="12"
               HorizontalAlignment="Center" VerticalAlignment="Center"
               TextTrimming="CharacterEllipsis"
               Visibility="{Binding FolderSummaryText, Converter={StaticResource StringEmptyToCollapsed}}" />
</StackPanel>
```

- [ ] **Step 2: Add converter resource if missing**

Check `src/Prosmotr/Resources/AppResources.xaml` for `StringEmptyToCollapsed`. If it does not exist, add inside `<ResourceDictionary>`:

```xml
<converters:StringEmptyToCollapsedConverter x:Key="StringEmptyToCollapsed" />
```

If the converter type does not exist, add a new converter in `src/Prosmotr/Converters/StringEmptyToCollapsedConverter.cs`:

```csharp
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Prosmotr.Converters;

[ValueConversion(typeof(string), typeof(Visibility))]
public sealed class StringEmptyToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

And reference the namespace in `App.xaml`:

```xml
xmlns:converters="clr-namespace:Prosmotr.Converters"
```

- [ ] **Step 3: Build to verify XAML**

Run: `dotnet build src\Prosmotr\Prosmotr.csproj -c Debug`
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/Prosmotr/Views/MainWindow.xaml src/Prosmotr/Resources/AppResources.xaml src/Prosmotr/Converters/StringEmptyToCollapsedConverter.cs src/Prosmotr/Converters/StringEmptyToCollapsedConverter.cs
git commit -m "feat: show folder summary under status bar"
```

---

### Task 3: Show summary in fullscreen info overlay

**Files:**

- Modify: `src/Prosmotr/Views/MainWindow.xaml:150-160`
- Modify: `src/Prosmotr/Views/VideoViewerView.xaml.cs:675-693`

**Interfaces:**

- Consumes: `MainViewModel.FolderSummaryText`.
- Produces: updated fullscreen overlay text for both photo and video.

- [ ] **Step 1: Update MainWindow fullscreen overlay**

Find the existing fullscreen info `Border` in `MainWindow.xaml`:

```xml
<Border Grid.Row="0" Grid.Column="1" VerticalAlignment="Top" HorizontalAlignment="Center" Margin="0,14,0,0"
        Background="#D91A1A1A" CornerRadius="6" Padding="14,6"
        IsHitTestVisible="False"
        Visibility="{Binding ShowFullscreenInfo, Converter={StaticResource BoolToVis}}">
    <TextBlock Text="{Binding StatusText}" Foreground="White" FontSize="13" Opacity="0.9" />
</Border>
```

Change the inner `TextBlock` to a `StackPanel`:

```xml
<Border Grid.Row="0" Grid.Column="1" VerticalAlignment="Top" HorizontalAlignment="Center" Margin="0,14,0,0"
        Background="#D91A1A1A" CornerRadius="6" Padding="14,6"
        IsHitTestVisible="False"
        Visibility="{Binding ShowFullscreenInfo, Converter={StaticResource BoolToVis}}">
    <StackPanel Orientation="Vertical" HorizontalAlignment="Center">
        <TextBlock Text="{Binding StatusText}" Foreground="White" FontSize="13" Opacity="0.9" />
        <TextBlock Text="{Binding FolderSummaryText}" Foreground="White" FontSize="12" Opacity="0.75"
                   HorizontalAlignment="Center"
                   Visibility="{Binding FolderSummaryText, Converter={StaticResource StringEmptyToCollapsed}}" />
    </StackPanel>
</Border>
```

- [ ] **Step 2: Update video fullscreen overlay**

In `VideoViewerView.xaml.cs`, locate the `UpdateInfo` method. It currently sets:

```csharp
InfoText.Text = _mainVm?.StatusText ?? string.Empty;
```

Change to:

```csharp
var summary = _mainVm?.FolderSummaryText;
if (!string.IsNullOrEmpty(summary))
    InfoText.Text = $"{_mainVm?.StatusText} · {summary}";
else
    InfoText.Text = _mainVm?.StatusText ?? string.Empty;
```

Also add `e.PropertyName == nameof(MainViewModel.FolderSummaryText)` to the property-changed handler that calls `UpdateInfo`, so changes refresh the overlay live.

- [ ] **Step 3: Build to verify XAML and C#**

Run: `dotnet build src\Prosmotr\Prosmotr.csproj -c Debug`
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/Prosmotr/Views/MainWindow.xaml src/Prosmotr/Views/VideoViewerView.xaml.cs
git commit -m "feat: show folder summary in fullscreen overlay"
```

---

### Task 4: Verify end-to-end

**Files:**

- All above.

- [ ] **Step 1: Run unit tests**

Run: `dotnet test tests\Prosmotr.Tests\Prosmotr.Tests.csproj`
Expected: all tests pass.

- [ ] **Step 2: Publish to app folder**

Run:

```powershell
Get-Process -Name "Prosmotr" -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item -Path "app" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "src\Prosmotr\bin", "src\Prosmotr\obj" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "$env:TEMP\Prosmotr*" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "$env:TEMP\\.NET*" -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish src\Prosmotr\Prosmotr.csproj -c Release -o app
```

Expected: publish succeeds.

- [ ] **Step 3: Run app and visually check**

Run: `app\Prosmotr.exe <path-to-folder-with-mixed-media>`
Confirm:

- Bottom status bar shows current file info plus a smaller line with `N фото, N видео, N GIF`.
- Enter fullscreen — top info overlay includes the summary (for photos directly, for videos after `UpdateInfo` refresh).
- Empty startup screen hides the summary line.

- [ ] **Step 4: Commit verification notes (optional)**

No commit required unless user asks for test-evidence-report.

---

## Self-review

1. **Spec coverage:**
   - Separate row under status line → Task 2.
   - Counters for photo, video, GIF → Task 1 helper.
   - Hidden when no folder open → Task 1 empty check + Task 2 converter.
   - Shown in fullscreen overlay → Task 3.
   - Russian copy format `12 фото, 3 видео, 1 GIF` → Task 1 strings.

2. **Placeholder scan:** No TBD/TODO. All code blocks concrete. Only dynamic check is whether `StringEmptyToCollapsed` already exists.

3. **Type consistency:** `FolderSummaryText` is `string` everywhere; helper signature `BuildFolderSummaryText(int, int, int)` matches test usage.
