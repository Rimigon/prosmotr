using Prosmotr.Models;
using Prosmotr.Services;
using Xunit;

namespace Prosmotr.Tests;

/// <summary>Валидация настроек из (возможно битого) settings.json: невалидные значения → дефолты.</summary>
public sealed class SettingsValidationTests
{
    [Fact]
    public void NegativeSeekStep_ReplacedWithDefault()
    {
        var s = new AppSettings { SeekStepSeconds = -5 };
        SettingsService.ValidateAndFix(s);
        Assert.Equal(new AppSettings().SeekStepSeconds, s.SeekStepSeconds); // дефолт = 5
    }

    [Fact]
    public void OutOfRangePlaybackRate_ReplacedWithDefault()
    {
        var s = new AppSettings { DefaultPlaybackRate = 99f }; // вне [0.25, 5.0]
        SettingsService.ValidateAndFix(s);
        Assert.Equal(new AppSettings().DefaultPlaybackRate, s.DefaultPlaybackRate);
    }

    [Fact]
    public void ZeroSlideshowInterval_ReplacedWithDefault()
    {
        var s = new AppSettings { SlideshowIntervalSeconds = 0 }; // вне [1, 60]
        SettingsService.ValidateAndFix(s);
        Assert.Equal(new AppSettings().SlideshowIntervalSeconds, s.SlideshowIntervalSeconds);
    }

    [Fact]
    public void SeekStep_AboveSliderMax_ReplacedWithDefault()
    {
        // Граница согласована со слайдером [1,30]: 60 теперь невалидно → дефолт.
        var s = new AppSettings { SeekStepSeconds = 60 };
        SettingsService.ValidateAndFix(s);
        Assert.Equal(new AppSettings().SeekStepSeconds, s.SeekStepSeconds);
    }

    [Fact]
    public void ValidValues_AreKept()
    {
        var s = new AppSettings
        {
            SeekStepSeconds = 12,
            DefaultPlaybackRate = 1.5f,
            SlideshowIntervalSeconds = 30
        };
        SettingsService.ValidateAndFix(s);
        Assert.Equal(12, s.SeekStepSeconds);
        Assert.Equal(1.5f, s.DefaultPlaybackRate);
        Assert.Equal(30, s.SlideshowIntervalSeconds);
    }

    [Fact]
    public void OutOfRangeMiniTimelineThreshold_ReplacedWithDefault()
    {
        var s = new AppSettings { MiniTimelineThresholdMinutes = 200 }; // вне [1, 120]
        SettingsService.ValidateAndFix(s);
        Assert.Equal(new AppSettings().MiniTimelineThresholdMinutes, s.MiniTimelineThresholdMinutes);
    }

    [Fact]
    public void ZeroMiniTimelineThreshold_ReplacedWithDefault()
    {
        var s = new AppSettings { MiniTimelineThresholdMinutes = 0 }; // вне [1, 120]
        SettingsService.ValidateAndFix(s);
        Assert.Equal(new AppSettings().MiniTimelineThresholdMinutes, s.MiniTimelineThresholdMinutes);
    }

    [Fact]
    public void Default_MiniTimelineSettings_AreExpected()
    {
        var s = new AppSettings();
        Assert.True(s.ShowMiniTimeline);
        Assert.Equal(20, s.MiniTimelineThresholdMinutes);
    }

    [Fact]
    public void ValidMiniTimelineThreshold_IsKept()
    {
        var s = new AppSettings { MiniTimelineThresholdMinutes = 45 };
        SettingsService.ValidateAndFix(s);
        Assert.Equal(45, s.MiniTimelineThresholdMinutes);
    }

    [Fact]
    public void MultipleInvalidValues_AllReplacedIndependently()
    {
        var defaults = new AppSettings();
        var s = new AppSettings
        {
            SeekStepSeconds = -1,         // невалидно
            DefaultPlaybackRate = 1.25f,  // валидно — должно сохраниться
            SlideshowIntervalSeconds = 999, // невалидно
            MiniTimelineThresholdMinutes = 999 // невалидно
        };
        SettingsService.ValidateAndFix(s);
        Assert.Equal(defaults.SeekStepSeconds, s.SeekStepSeconds);
        Assert.Equal(1.25f, s.DefaultPlaybackRate);
        Assert.Equal(defaults.SlideshowIntervalSeconds, s.SlideshowIntervalSeconds);
        Assert.Equal(defaults.MiniTimelineThresholdMinutes, s.MiniTimelineThresholdMinutes);
    }
}
