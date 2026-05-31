using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Prosmotr.Models;
using Prosmotr.ViewModels;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;

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

        // Контекстное меню по правому клику (поворот, масштаб, копирование, действия с файлом).
        ContextMenu = new ContextMenu();
        ContextMenuOpening += OnContextMenuOpening;
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
            Zoom.SetMode(ImageViewMode.Fit); // сброс зума/панорамы под новое изображение
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
