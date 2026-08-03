namespace Prosmotr.Infrastructure;

/// <summary>Позиция таймлайна: X-координата мыши на слайдере → миллисекунды видео.</summary>
public static class TimelineMath
{
    /// <summary>Пропорционально отобразить позицию мыши (в DIP) в время (мс).
    /// x — смещение от левого края слайдера; width — ActualWidth слайдера (0 → 0);
    /// lengthMs — длительность видео (0 → 0). Результат клампится в [0, lengthMs].</summary>
    public static double MapSliderXToMs(double x, double width, double lengthMs)
    {
        if (width <= 0 || lengthMs <= 0) return 0;
        var ratio = Math.Clamp(x / width, 0.0, 1.0);
        return ratio * lengthMs;
    }
}
