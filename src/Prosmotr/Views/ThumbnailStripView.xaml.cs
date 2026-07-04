using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Prosmotr.Infrastructure;
using Prosmotr.ViewModels;

namespace Prosmotr.Views;

public partial class ThumbnailStripView : UserControl
{
    private const double ScrollBarThickness = 28;
    private const double MinThumbLength = 72;
    private const double MaxThumbRatio = 0.55;

    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(ThumbnailStripView),
            new PropertyMetadata(Orientation.Horizontal, OnOrientationChanged));

    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    private ScrollViewer? _scrollViewer;
    private ScrollBar? _horizontalScrollBar;
    private ScrollBar? _verticalScrollBar;
    private ThumbnailStripViewModel? _viewModel;

    public ThumbnailStripView()
    {
        InitializeComponent();
        List.SelectionChanged += OnSelectionChanged;
        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
        UpdateScrollBars();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = e.NewValue as ThumbnailStripViewModel;

        if (_viewModel != null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            Dispatcher.BeginInvoke(UpdateScrollBarThumbSize, DispatcherPriority.Render);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ThumbnailStripViewModel.Items) or nameof(ThumbnailStripViewModel.Selected))
            Dispatcher.BeginInvoke(UpdateScrollBarThumbSize, DispatcherPriority.Render);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _scrollViewer = List.FindChild<ScrollViewer>();
        AttachScrollBarHandlers();
        Dispatcher.BeginInvoke(UpdateScrollBarThumbSize, DispatcherPriority.Render);
    }

    private static void OnOrientationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (ThumbnailStripView)d;
        view.UpdateScrollBars();
        if (view.IsLoaded)
            view.Dispatcher.BeginInvoke(view.AttachScrollBarHandlers, DispatcherPriority.Render);
    }

    private void AttachScrollBarHandlers()
    {
        if (_scrollViewer == null) return;

        foreach (var scrollBar in _scrollViewer.FindChildren<ScrollBar>())
        {
            scrollBar.PreviewMouseLeftButtonDown -= OnScrollBarPreviewMouseDown;
            scrollBar.PreviewMouseLeftButtonDown += OnScrollBarPreviewMouseDown;

            bool horizontal = scrollBar.Orientation == Orientation.Horizontal;
            if (horizontal)
            {
                scrollBar.Height = ScrollBarThickness;
                scrollBar.MinHeight = ScrollBarThickness;
            }
            else
            {
                scrollBar.Width = ScrollBarThickness;
                scrollBar.MinWidth = ScrollBarThickness;
            }
        }

        _horizontalScrollBar = _scrollViewer.FindChildren<ScrollBar>()
            .FirstOrDefault(s => s.Orientation == Orientation.Horizontal);
        _verticalScrollBar = _scrollViewer.FindChildren<ScrollBar>()
            .FirstOrDefault(s => s.Orientation == Orientation.Vertical);

        UpdateScrollBarThumbSize();
    }

    private void UpdateScrollBarThumbSize()
    {
        var vm = DataContext as ThumbnailStripViewModel;
        int count = vm?.Items.Count ?? 0;
        if (count <= 0) return;

        double trackLength = Orientation == Orientation.Horizontal
            ? (_horizontalScrollBar?.ActualWidth ?? _scrollViewer?.ActualWidth ?? 0)
            : (_verticalScrollBar?.ActualHeight ?? _scrollViewer?.ActualHeight ?? 0);

        if (trackLength <= 0) return;

        // Чем больше файлов, тем длиннее бегунок, но не более 45% трека.
        double itemRatio = Math.Min(1.0, 10.0 / Math.Max(1, count));
        double targetLength = Math.Max(MinThumbLength, trackLength * itemRatio);
        targetLength = Math.Min(targetLength, trackLength * MaxThumbRatio);

        var bar = Orientation == Orientation.Horizontal ? _horizontalScrollBar : _verticalScrollBar;
        var thumb = bar?.FindChild<Thumb>();
        if (thumb == null) return;

        thumb.Style = (Style)FindResource("ThumbnailScrollThumbStyle");

        if (Orientation == Orientation.Horizontal)
            thumb.MinWidth = targetLength;
        else
            thumb.MinHeight = targetLength;
    }

    private void OnScrollBarPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ScrollBar scrollBar || _scrollViewer == null) return;
        var track = scrollBar.FindChild<Track>();
        if (track?.Thumb == null) return;

        var pos = e.GetPosition(track);
        bool horizontal = scrollBar.Orientation == Orientation.Horizontal;

        double thumbPos = horizontal
            ? track.Thumb.TranslatePoint(new Point(0, 0), track).X
            : track.Thumb.TranslatePoint(new Point(0, 0), track).Y;
        double thumbSize = horizontal ? track.Thumb.ActualWidth : track.Thumb.ActualHeight;
        double clickPos = horizontal ? pos.X : pos.Y;

        // Клик по самому бегунку оставляем системе (перетаскивание).
        if (clickPos >= thumbPos && clickPos <= thumbPos + thumbSize)
            return;

        double trackSize = horizontal ? track.ActualWidth : track.ActualHeight;
        double usable = Math.Max(1, trackSize - thumbSize);
        double ratio = Math.Max(0, Math.Min(1, (clickPos - thumbSize / 2) / usable));

        if (horizontal)
        {
            double target = ratio * _scrollViewer.ScrollableWidth;
            _scrollViewer.ScrollToHorizontalOffset(target);
        }
        else
        {
            double target = ratio * _scrollViewer.ScrollableHeight;
            _scrollViewer.ScrollToVerticalOffset(target);
        }

        e.Handled = true;
    }

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

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (List.SelectedItem != null)
            List.ScrollIntoView(List.SelectedItem);
    }
}
