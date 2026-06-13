using System.Globalization;
using System.Windows.Data;
using Prosmotr.Converters;
using Xunit;

namespace Prosmotr.Tests;

/// <summary>Чистая логика форматирующих конвертеров (размер файла, время).</summary>
public sealed class ConverterTests
{
    [Theory]
    [InlineData(0L, "0 Б")]
    [InlineData(512L, "512 Б")]
    [InlineData(1023L, "1023 Б")]
    [InlineData(1024L, "1 КБ")]
    [InlineData(1048576L, "1 МБ")]            // 1024^2
    [InlineData(1073741824L, "1 ГБ")]         // 1024^3
    [InlineData(1099511627776L, "1 ТБ")]      // 1024^4
    public void FileSize_FormatsWholeUnits(long bytes, string expected)
    {
        Assert.Equal(expected, FileSizeConverter.Format(bytes));
    }

    [Fact]
    public void FileSize_FormatsFractionalRespectingCulture()
    {
        // 1.5 КБ — десятичный разделитель зависит от культуры (рус. «,», англ. «.»).
        var expected = $"{(1.5).ToString("0.##", CultureInfo.CurrentCulture)} КБ";
        Assert.Equal(expected, FileSizeConverter.Format(1536));
    }

    [Fact]
    public void FileSize_CapsAtTerabytes()
    {
        // Больше ТБ всё равно остаётся в ТБ (нет ПБ в таблице единиц).
        var result = FileSizeConverter.Format(5L * 1099511627776L);
        Assert.EndsWith("ТБ", result);
    }

    private static readonly MillisecondsToTimeConverter Time = new();

    [Theory]
    [InlineData(0.0, "0:00")]
    [InlineData(5000.0, "0:05")]
    [InlineData(65000.0, "1:05")]
    [InlineData(600000.0, "10:00")]
    public void Milliseconds_FormatsMinutesSeconds(double ms, string expected)
    {
        Assert.Equal(expected, Time.Convert(ms, typeof(string), null!, CultureInfo.CurrentCulture));
    }

    [Fact]
    public void Milliseconds_FormatsHoursWhenOverAnHour()
    {
        // 1ч 01м 01с
        Assert.Equal("1:01:01", Time.Convert(3661000.0, typeof(string), null!, CultureInfo.CurrentCulture));
    }

    [Fact]
    public void Milliseconds_NegativeAndNaN_TreatedAsZero()
    {
        Assert.Equal("0:00", Time.Convert(-100.0, typeof(string), null!, CultureInfo.CurrentCulture));
        Assert.Equal("0:00", Time.Convert(double.NaN, typeof(string), null!, CultureInfo.CurrentCulture));
    }

    [Fact]
    public void Milliseconds_ConvertBack_DoesNothing()
    {
        Assert.Same(Binding.DoNothing, Time.ConvertBack("x", typeof(double), null!, CultureInfo.CurrentCulture));
    }
}
