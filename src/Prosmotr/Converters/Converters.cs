using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Prosmotr.Converters;

/// <summary>true → Collapsed, false → Visible (обратная видимость).</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) =>
        value is Visibility.Collapsed;
}

/// <summary>true → Visible, false → Collapsed.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) =>
        value is Visibility.Visible;
}

/// <summary>Все значения true → Visible, иначе Collapsed.</summary>
public sealed class BoolAndToVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        foreach (var v in values)
        {
            if (v is not bool b || !b)
                return Visibility.Collapsed;
        }
        return Visibility.Visible;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>null → Collapsed, иначе Visible.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) =>
        value == null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) =>
        Binding.DoNothing;
}

/// <summary>Не пустая строка → Visible, иначе Collapsed.</summary>
public sealed class StringNotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) =>
        !string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) =>
        throw new NotSupportedException();
}

/// <summary>Миллисекунды (double) → «m:ss» или «h:mm:ss».</summary>
public sealed class MillisecondsToTimeConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        var ms = value is double d ? d : 0;
        if (ms < 0 || double.IsNaN(ms)) ms = 0;
        var span = TimeSpan.FromMilliseconds(ms);
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes}:{span.Seconds:00}";
    }

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Сравнение значения с параметром (для радио/подсветки): равно → true.</summary>
public sealed class EqualityConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) =>
        Equals(value?.ToString(), p?.ToString());

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Скорость воспроизведения (float) → «1,5×».</summary>
public sealed class PlaybackRateToStringConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        var r = value is float f ? f : (value is double dd ? (float)dd : 1f);
        return r.ToString("0.##", CultureInfo.CurrentCulture) + "×";
    }

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Тема (enum) → читаемое название.</summary>
public sealed class ThemeToStringConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) =>
        value?.ToString() switch
        {
            "Light" => "Светлая",
            "Dark" => "Тёмная",
            _ => "Системная"
        };

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Положение ленты миниатюр (enum) → читаемое название.</summary>
public sealed class StripPositionToStringConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) =>
        value?.ToString() == "Left" ? "Слева" : "Снизу";

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Поле сортировки (enum) → читаемое название.</summary>
public sealed class SortFieldToStringConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) =>
        value?.ToString() switch
        {
            "DateModified" => "Дата изменения",
            "DateCreated" => "Дата создания",
            "Size" => "Размер",
            "Type" => "Тип",
            "Duration" => "Продолжительность",
            _ => "Имя"
        };

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}
