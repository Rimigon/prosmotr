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

        // Отслеживаем реальное появление Source у Image, чтобы пересчитать зум
        // даже в случаях, когда PropertyChanged VM пришёл до привязки обработчика
        // (например, синхронный кэш-хит при смене фото в переиспользуемом View).
        AddSourceChangedHandler(StaticImage);
        AddSourceChangedHandler(AnimatedImage);

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
        // Останавливаем анимацию GIF, чтобы декодер освободил ресурсы (issue: переход GIF→JPG).
        AnimationBehavior.SetSourceUri(AnimatedImage, null);
        _vm = null;
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
