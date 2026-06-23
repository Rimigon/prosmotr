using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Prosmotr.Models;
using Prosmotr.ViewModels;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;
using XamlAnimatedGif;

namespace Prosmotr.Views;

public partial class ImageViewerView : UserControl
{
    private ImageViewerViewModel? _vm;

    public ImageViewerView()
    {
        InitializeComponent();
        Zoom.ZoomChanged += OnZoomChanged; // собственный контрол — подписка один раз
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;

        // Статичные фото: иногда PropertyChanged приходит до привязки обработчика,
        // поэтому отслеживаем реальное появление Source у StaticImage.
        // Для анимированных GIF используем событие Loaded от XamlAnimatedGif,
        // т.к. DependencyPropertyDescriptor на Source анимированного GIF
        // может давать множественные ложные срабатывания.
        AddSourceChangedHandler(StaticImage);
        AnimationBehavior.AddLoadedHandler(AnimatedImage, OnGifLoaded);

        // Контекстное меню по правому клику (поворот, масштаб, копирование, действия с файлом).
        ContextMenu = new ContextMenu();
        ContextMenuOpening += OnContextMenuOpening;
    }

    private void AddSourceChangedHandler(Image image)
    {
        DependencyPropertyDescriptor
            .FromProperty(Image.SourceProperty, typeof(Image))
            .AddValueChanged(image, OnImageSourceChanged);
    }

    private void OnImageSourceChanged(object? sender, EventArgs e)
    {
        // Source мог обновиться на null при смене VM — тогда ничего не делаем.
        if (sender is Image { Source: not null })
        {
            // Ждём, пока binding и макет применятся, затем вписываем изображение.
            Dispatcher.BeginInvoke(() => Zoom.SetMode(ImageViewMode.Fit), DispatcherPriority.Render);
        }
    }

    private void OnGifLoaded(object? sender, RoutedEventArgs e)
    {
        // XamlAnimatedGif закончил декодирование и установил Image.Source.
        // Показываем GIF сразу, не дожидаясь дополнительных уведомлений.
        if (sender is Image { Source: not null })
            Dispatcher.BeginInvoke(() => Zoom.SetMode(ImageViewMode.Fit), DispatcherPriority.Render);
    }

    private MainViewModel? MainVm => Window.GetWindow(this)?.DataContext as MainViewModel;

    private void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (ContextMenu is not ContextMenu menu) { e.Handled = true; return; }
        var items = menu.Items;
        items.Clear();
        var main = MainVm;
        var vm = _vm;

        if (main != null) MediaContextMenu.AddNavigation(items, main);

        if (vm != null)
        {
            items.Add(new Separator());
            items.Add(MediaContextMenu.Item("Повернуть влево",
                () => vm.RotateLeftCommand.Execute(null), icon: SymbolRegular.ArrowRotateCounterclockwise24));
            items.Add(MediaContextMenu.Item("Повернуть вправо",
                () => vm.RotateRightCommand.Execute(null), icon: SymbolRegular.ArrowRotateClockwise24));
            if (vm.CanSaveRotation)
                items.Add(MediaContextMenu.Item("Сохранить поворот в файл",
                    () => vm.SaveRotationCommand.Execute(null), icon: SymbolRegular.Save24));

            items.Add(new Separator());
            items.Add(MediaContextMenu.Item("По размеру окна",
                () => vm.FitToWindowCommand.Execute(null), icon: SymbolRegular.FullScreenMaximize24));
            items.Add(MediaContextMenu.Item("Реальный размер (100%)",
                () => vm.ActualSizeCommand.Execute(null)));
            items.Add(MediaContextMenu.Item("Заполнить",
                () => vm.FillWindowCommand.Execute(null), icon: SymbolRegular.ArrowMaximize24));
            items.Add(MediaContextMenu.Item("Копировать изображение",
                () => vm.CopyImageCommand.Execute(null), vm.CanCopyImage, SymbolRegular.ImageCopy24));
        }

        if (main != null)
        {
            items.Add(new Separator());
            MediaContextMenu.AddFileActions(items, main);
            items.Add(new Separator());
            items.Add(MediaContextMenu.Item("Полный экран",
                () => main.ToggleFullScreenCommand.Execute(null), icon: SymbolRegular.FullScreenMaximize24));
        }
    }

    // ContentControl переиспользует этот View при смене фото (тот же тип VM),
    // поэтому переинициализируемся по смене DataContext, а не только по Loaded.
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachVm();
        if (DataContext is ImageViewerViewModel vm)
        {
            _vm = vm;
            _vm.PropertyChanged += OnVmPropertyChanged;
            vm.ZoomInRequested += OnZoomIn;
            vm.ZoomOutRequested += OnZoomOut;
            vm.ViewModeRequested += OnViewMode;
            vm.ReleaseFileHandleRequested += OnReleaseFileHandleRequested;
            vm.RestoreFileHandleRequested += OnRestoreFileHandleRequested;

            // XamlAnimatedGif плохо перезагружает анимацию при переиспользовании View,
            // если SourceUri задаётся только через attached-property binding в XAML.
            // Явно сбрасываем и устанавливаем SourceUri из code-behind.
            if (vm.IsAnimated)
            {
                AnimationBehavior.SetSourceUri(AnimatedImage, null);
                AnimationBehavior.SetSourceUri(AnimatedImage, vm.AnimatedSource);
            }

            // Немедленно скрываем содержимое, чтобы старое фото не мелькало
            // со старым масштабом до смены источника. Пересчёт зума откладываем
            // на Render-приоритет, когда binding уже применит новый Image.Source.
            Zoom.HideContent();
            Dispatcher.BeginInvoke(() =>
            {
                Zoom.ResetContent(ImageViewMode.Fit);
                Zoom.SetMode(ImageViewMode.Fit);

                // Страховка: если binding уже успел применить Source раньше
                // Render-приоритета (синхронный кэш-хит), пересчитываем зум ещё раз.
                if (StaticImage.Source != null || AnimatedImage.Source != null)
                    Zoom.SetMode(ImageViewMode.Fit);
            }, DispatcherPriority.Render);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => DetachVm();

    private void DetachVm()
    {
        if (_vm == null) return;
        _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm.ZoomInRequested -= OnZoomIn;
        _vm.ZoomOutRequested -= OnZoomOut;
        _vm.ViewModeRequested -= OnViewMode;
        _vm.ReleaseFileHandleRequested -= OnReleaseFileHandleRequested;
        _vm.RestoreFileHandleRequested -= OnRestoreFileHandleRequested;
        // Останавливаем анимацию GIF, чтобы декодер освободил ресурсы (issue: переход GIF→JPG).
        AnimationBehavior.SetSourceUri(AnimatedImage, null);
        AnimatedImage.Source = null;
        _vm = null;
    }

    /// <summary>XamlAnimatedGif держит FileStream открытым во время анимации.
    /// Перед удалением файла сбрасываем SourceUri, чтобы освободить handle.
    /// Также сбрасываем Image.Source, чтобы Animator.Dispose отработал синхронно.</summary>
    private void OnReleaseFileHandleRequested(object? sender, EventArgs e)
    {
        AnimationBehavior.SetSourceUri(AnimatedImage, null);
        AnimatedImage.Source = null;
    }

    /// <summary>Если удаление не удалось, восстанавливаем SourceUri,
    /// чтобы XamlAnimatedGif снова загрузил GIF.</summary>
    private void OnRestoreFileHandleRequested(object? sender, EventArgs e)
    {
        if (_vm is null) return;
        AnimationBehavior.SetSourceUri(AnimatedImage, _vm.AnimatedSource);
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ImageViewerViewModel.Image) or nameof(ImageViewerViewModel.AnimatedSource))
        {
            // Даём WPF время применить binding и обновить Image.Source,
            // затем пересчитываем зум от реальных размеров картинки.
            Dispatcher.BeginInvoke(() => Zoom.SetMode(ImageViewMode.Fit), DispatcherPriority.Render);
        }
    }

    private void OnZoomIn(object? sender, EventArgs e) => Zoom.ZoomIn();
    private void OnZoomOut(object? sender, EventArgs e) => Zoom.ZoomOut();
    private void OnViewMode(object? sender, ImageViewMode mode) => Zoom.SetMode(mode);

    private void OnZoomChanged(object? sender, double percent)
    {
        if (_vm != null) _vm.ZoomPercent = Math.Round(percent);
    }
}
