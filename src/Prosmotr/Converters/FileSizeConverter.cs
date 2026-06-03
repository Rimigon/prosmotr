using System.Globalization;
using System.Windows.Data;

namespace Prosmotr.Converters;

/// <summary>Конвертер <see cref="long"/> (байты) → человекочитаемый размер файла.</summary>
[ValueConversion(typeof(long), typeof(string))]
public sealed class FileSizeConverter : IValueConverter
{
    public static string Format(long bytes)
    {
        string[] units = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
        double size = bytes;
        int u = 0;
        while (size >= 1024 && u < units.Length - 1) { size /= 1024; u++; }
        return u == 0
            ? $"{bytes} Б"
            : $"{size.ToString("0.##", CultureInfo.CurrentCulture)} {units[u]}";
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is long l) return Format(l);
        if (value is int i) return Format(i);
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
