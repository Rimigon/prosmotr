using Prosmotr.Infrastructure;
using Xunit;

namespace Prosmotr.Tests;

/// <summary>Отображение X-координаты мыши на слайдере таймлайна в миллисекунды видео.</summary>
public sealed class TimelineMathTests
{
    [Fact] public void Map_LeftEdge_IsZero() => Assert.Equal(0, TimelineMath.MapSliderXToMs(0, 100, 10_000));
    [Fact] public void Map_Middle_IsHalf() => Assert.Equal(5_000, TimelineMath.MapSliderXToMs(50, 100, 10_000));
    [Fact] public void Map_Proportional() => Assert.Equal(2_500, TimelineMath.MapSliderXToMs(25, 100, 10_000));
    [Fact] public void Map_RightEdge_IsLength() => Assert.Equal(10_000, TimelineMath.MapSliderXToMs(100, 100, 10_000));
    [Fact] public void Map_ClampsBeyondWidth_ToLength() => Assert.Equal(10_000, TimelineMath.MapSliderXToMs(150, 100, 10_000));
    [Fact] public void Map_ClampsNegativeX_ToZero() => Assert.Equal(0, TimelineMath.MapSliderXToMs(-10, 100, 10_000));
    [Fact] public void Map_ZeroWidth_ReturnsZero() => Assert.Equal(0, TimelineMath.MapSliderXToMs(50, 0, 10_000));
    [Fact] public void Map_ZeroLength_ReturnsZero() => Assert.Equal(0, TimelineMath.MapSliderXToMs(50, 100, 0));
}
