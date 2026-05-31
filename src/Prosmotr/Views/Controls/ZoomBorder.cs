using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Prosmotr.Models;

namespace Prosmotr.Views.Controls;

/// <summary>
/// Область просмотра изображения с зумом (колесо/двойной клик/кнопки), панорамированием
/// и режимами подгонки (по размеру окна / 100% / заполнить). Масштабирует свой Child
/// через RenderTransform, поэтому работает с любым содержимым (в т.ч. повёрнутым).
/// </summary>
public class ZoomBorder : Border
{
    private const double MinScale = 0.05;
    private const double MaxScale = 40.0;
    private const double WheelStep = 1.15;
    private const double ButtonStep = 1.25;

    private readonly ScaleTransform _scale = new(1, 1);
    private readonly TranslateTransform _translate = new(0, 0);

    private FrameworkElement? _child;
    private Point _dragStart;
    private bool _dragging;
    private ImageViewMode _mode = ImageViewMode.Fit;
    private bool _free; // пользователь зумил/двигал вручную — не пересчитывать режим при ресайзе
    private Size _lastNaturalSize;

    /// <summary>Текущий масштаб в процентах (для индикатора).</summary>
    public event EventHandler<double>? ZoomChanged;

    public ZoomBorder()
    {
        ClipToBounds = true;
        Background = Brushes.Transparent; // чтобы ловить мышь на пустых местах
        Focusable = true;
        SizeChanged += OnSizeChanged;
        LayoutUpdated += OnLayoutUpdated;
    }

    public override UIElement Child
    {
        get => base.Child;
        set
        {
            if (_child != null)
                _child.SizeChanged -= OnChildSizeChanged;

            if (value != null && !ReferenceEquals(value, base.Child))
            {
                var group = new TransformGroup();
                group.Children.Add(_scale);
                group.Children.Add(_translate);
                value.RenderTransform = group;
                value.RenderTransformOrigin = new Point(0, 0);

                _child = value as FrameworkElement;
                if (_child != null)
                    _child.SizeChanged += OnChildSizeChanged;
            }
            base.Child = value;
            _free = false;
            _lastNaturalSize = Size.Empty;
            ApplyMode();
        }
    }

    private void OnChildSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_free) ApplyMode();
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (_free || _child == null) return;
        var natural = GetNaturalContentSize(_child);
        if (Math.Abs(natural.Width - _lastNaturalSize.Width) > 0.5 ||
            Math.Abs(natural.Height - _lastNaturalSize.Height) > 0.5)
        {
            _lastNaturalSize = natural;
            ApplyMode();
        }
    }

    public void SetMode(ImageViewMode mode)
    {
        _mode = mode;
        _free = false;
        ApplyMode();
    }

    public void ZoomIn() => ZoomAt(ButtonStep, new Point(ActualWidth / 2, ActualHeight / 2));
    public void ZoomOut() => ZoomAt(1 / ButtonStep, new Point(ActualWidth / 2, ActualHeight / 2));

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_free) ApplyMode();
    }

    /// <summary>Применить выбранный режим подгонки (вписать / 100% / заполнить) и центрировать.</summary>
    public void ApplyMode()
    {
        if (_child is null) return;

        var natural = GetNaturalContentSize(_child);
        double nw = natural.Width;
        double nh = natural.Height;

        if (nw <= 0 || nh <= 0 || ActualWidth <= 0 || ActualHeight <= 0) return;

        double scale = _mode switch
        {
            ImageViewMode.ActualSize => 1.0,
            ImageViewMode.Fill => Math.Max(ActualWidth / nw, ActualHeight / nh),
            _ => Math.Min(ActualWidth / nw, ActualHeight / nh) // Fit
        };

        _scale.ScaleX = _scale.ScaleY = scale;
        // Центрируем относительно натурального размера содержимого (не layout-границ Canvas),
        // иначе картинка уезжает за края.
        CenterContent(nw, nh, scale);
        _lastNaturalSize = new Size(nw, nh);
        RaiseZoom();
    }

    /// <summary>
    /// Возвращает "натуральные" размеры содержимого (без ограничений родителя).
    /// Для <see cref="Image"/> берёт размеры <see cref="ImageSource"/> с учётом <see cref="UIElement.LayoutTransform"/>.
    /// </summary>
    private static Size GetNaturalContentSize(FrameworkElement element)
    {
        if (element is Image img && img.Source != null)
        {
            double w = img.Source.Width;
            double h = img.Source.Height;
            if (w > 0 && h > 0)
            {
                var size = new Size(w, h);
                if (img.LayoutTransform != null)
                {
                    var bounds = img.LayoutTransform.TransformBounds(new Rect(size));
                    size = new Size(bounds.Width, bounds.Height);
                }
                return size;
            }
        }

        if (element is Panel panel)
        {
            foreach (UIElement child in panel.Children)
            {
                if (child is FrameworkElement fe && child.Visibility != Visibility.Collapsed)
                {
                    var size = GetNaturalContentSize(fe);
                    if (size.Width > 0 && size.Height > 0)
                        return size;
                }
            }
        }

        // Fallback: если ничего не нашли — используем текущие размещённые размеры.
        return new Size(element.ActualWidth, element.ActualHeight);
    }

    private void CenterContent(double cw, double ch, double scale)
    {
        _translate.X = (ActualWidth - cw * scale) / 2;
        _translate.Y = (ActualHeight - ch * scale) / 2;
    }

    private void ZoomAt(double factor, Point center)
    {
        var newScale = Math.Clamp(_scale.ScaleX * factor, MinScale, MaxScale);
        factor = newScale / _scale.ScaleX;
        if (Math.Abs(factor - 1.0) < 0.0001) return;

        // Точка под курсором остаётся на месте.
        _translate.X = center.X - (center.X - _translate.X) * factor;
        _translate.Y = center.Y - (center.Y - _translate.Y) * factor;
        _scale.ScaleX = _scale.ScaleY = newScale;
        _free = true;
        RaiseZoom();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        ZoomAt(e.Delta > 0 ? WheelStep : 1 / WheelStep, e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.ClickCount == 2)
        {
            // Двойной клик: переключение между «по размеру окна» и 100%.
            if (Math.Abs(_scale.ScaleX - 1.0) < 0.01)
                SetMode(ImageViewMode.Fit);
            else
            {
                var p = e.GetPosition(this);
                ZoomAt(1.0 / _scale.ScaleX, p); // привести к 100% относительно точки
            }
            e.Handled = true;
            return;
        }

        _dragStart = e.GetPosition(this);
        _dragging = true;
        Cursor = Cursors.SizeAll;
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        var pos = e.GetPosition(this);
        _translate.X += pos.X - _dragStart.X;
        _translate.Y += pos.Y - _dragStart.Y;
        _dragStart = pos;
        _free = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _dragging = false;
        Cursor = Cursors.Arrow;
        ReleaseMouseCapture();
    }

    private void RaiseZoom() => ZoomChanged?.Invoke(this, _scale.ScaleX * 100.0);
}
