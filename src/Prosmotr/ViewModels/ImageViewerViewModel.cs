using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Prosmotr.Infrastructure;
using Prosmotr.Models;
using Prosmotr.Services;
using Prosmotr.Services.Abstractions;

namespace Prosmotr.ViewModels;

/// <summary>Просмотр изображения: загрузка, зум/режимы (через View), поворот и опц. сохранение.</summary>
public sealed partial class ImageViewerViewModel : ViewModelBase, IDisposable
{
    private readonly IImageCache _cache;
    private readonly IDialogService _dialog;
    private readonly INotificationService _notify;
    private CancellationTokenSource? _cts;

    public MediaItem Item { get; }
    public bool IsAnimated => Item.IsAnimated;
    public Uri? AnimatedSource
    {
        get
        {
            if (!IsAnimated) return null;
            return PathUri.ToUri(Item.FullPath); // устойчиво к # / % в пути
        }
    }

    [ObservableProperty] private ImageSource? _image;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private int _rotation; // 0 / 90 / 180 / 270
    [ObservableProperty] private ImageViewMode _viewMode = ImageViewMode.Fit;
    [ObservableProperty] private double _zoomPercent = 100;

    /// <summary>Сигналы для ZoomBorder во View (зум/режим задаются на уровне UI).</summary>
    public event EventHandler? ZoomInRequested;
    public event EventHandler? ZoomOutRequested;
    public event EventHandler<ImageViewMode>? ViewModeRequested;

    public bool CanSaveRotation => !IsAnimated && Rotation % 360 != 0 && CanEncode(Item.FullPath);

    public ImageViewerViewModel(MediaItem item, IImageCache cache, IDialogService dialog, INotificationService notify)
    {
        Item = item;
        _cache = cache;
        _dialog = dialog;
        _notify = notify;
    }

    public bool CanCopyImage => !IsAnimated && Image is BitmapSource;

    [RelayCommand(CanExecute = nameof(CanCopyImage))]
    private void CopyImage()
    {
        if (Image is not BitmapSource source) return;
        try
        {
            Clipboard.SetImage(source);
            _notify.Show("Изображение скопировано в буфер обмена.", NotificationKind.Success);
        }
        catch
        {
            _notify.Show("Не удалось скопировать изображение.", NotificationKind.Error);
        }
    }

    partial void OnImageChanged(ImageSource? value)
    {
        OnPropertyChanged(nameof(CanCopyImage));
        CopyImageCommand.NotifyCanExecuteChanged();
    }

    public async Task LoadAsync()
    {
        if (IsAnimated)
        {
            OnPropertyChanged(nameof(AnimatedSource));
            return; // анимированный GIF рисует View через XamlAnimatedGif
        }

        // Уже в кэше (например, предзагружено как сосед) — ставим синхронно, без мигания.
        if (_cache.TryGetLoaded(Item.FullPath, out var cached))
        {
            Image = cached;
            HasError = false;
            IsLoading = false;
            return;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        IsLoading = true;
        HasError = false;
        try
        {
            var img = await _cache.GetAsync(Item.FullPath, ct);
            if (ct.IsCancellationRequested) return;
            Image = img;
            HasError = img == null;
        }
        catch (OperationCanceledException)
        {
            // Загрузку отменили (переключились на другое фото) — это не ошибка.
        }
        catch
        {
            HasError = true;
        }
        finally
        {
            if (!ct.IsCancellationRequested) IsLoading = false;
        }
    }

    [RelayCommand] private void ZoomIn() => ZoomInRequested?.Invoke(this, EventArgs.Empty);
    [RelayCommand] private void ZoomOut() => ZoomOutRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void FitToWindow()
    {
        ViewMode = ImageViewMode.Fit;
        ViewModeRequested?.Invoke(this, ImageViewMode.Fit);
    }

    [RelayCommand]
    private void ActualSize()
    {
        ViewMode = ImageViewMode.ActualSize;
        ViewModeRequested?.Invoke(this, ImageViewMode.ActualSize);
    }

    [RelayCommand]
    private void FillWindow()
    {
        ViewMode = ImageViewMode.Fill;
        ViewModeRequested?.Invoke(this, ImageViewMode.Fill);
    }

    [RelayCommand] private void RotateLeft() => SetRotation((Rotation + 270) % 360);
    [RelayCommand] private void RotateRight() => SetRotation((Rotation + 90) % 360);

    private void SetRotation(int value)
    {
        Rotation = value;
        OnPropertyChanged(nameof(CanSaveRotation));
        SaveRotationCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanSaveRotation))]
    private async Task SaveRotationAsync()
    {
        if (Image is not BitmapSource source || Rotation % 360 == 0) return;

        var confirmed = await _dialog.ConfirmAsync(
            "Сохранить поворот",
            $"Перезаписать файл «{Item.FileName}» с учётом поворота на {Rotation}°?",
            "Сохранить", "Отмена");
        if (!confirmed) return;

        try
        {
            var rotated = new TransformedBitmap(source, new RotateTransform(Rotation));
            rotated.Freeze();

            var encoder = CreateEncoder(Item.FullPath);
            encoder.Frames.Add(BitmapFrame.Create(rotated));

            var tmp = Item.FullPath + ".tmp";
            try
            {
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                    encoder.Save(fs);

                File.Move(tmp, Item.FullPath, overwrite: true);
            }
            finally
            {
                try { File.Delete(tmp); } catch { }
            }

            // Сброс — теперь файл физически повёрнут.
            Rotation = 0;
            OnPropertyChanged(nameof(CanSaveRotation));
            SaveRotationCommand.NotifyCanExecuteChanged();

            // Перезагрузить изображение из обновлённого файла.
            Image = null;
            await LoadAsync();
        }
        catch
        {
            await _dialog.ConfirmAsync("Ошибка", "Не удалось сохранить файл.", "OK", "OK");
        }
    }

    private static bool CanEncode(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".jpg" or ".jpeg" or ".jpe" or ".jfif" or ".png" or ".bmp" or ".tif" or ".tiff";
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private static BitmapEncoder CreateEncoder(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => new PngBitmapEncoder(),
            ".bmp" => new BmpBitmapEncoder(),
            ".tif" or ".tiff" => new TiffBitmapEncoder(),
            _ => new JpegBitmapEncoder { QualityLevel = 95 }
        };
}
