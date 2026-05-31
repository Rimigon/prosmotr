using System.Windows;
using System.Windows.Controls;

namespace Prosmotr.Views;

public partial class ThumbnailStripView : UserControl
{
    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(ThumbnailStripView),
            new PropertyMetadata(Orientation.Horizontal, OnOrientationChanged));

    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public ThumbnailStripView()
    {
        InitializeComponent();
        List.SelectionChanged += OnSelectionChanged;
        UpdateScrollBars();
    }

    private static void OnOrientationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ThumbnailStripView)d).UpdateScrollBars();

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
