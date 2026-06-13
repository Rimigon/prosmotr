using System.IO;
using Prosmotr.Services;
using Xunit;

namespace Prosmotr.Tests;

/// <summary>Персистентность настроек: атомарная запись, перезагрузка, валидация битого файла.</summary>
public sealed class SettingsServiceTests
{
    [Fact]
    public void Save_PersistsAndReloads()
    {
        using var dir = new TempDir();

        using (var svc = new SettingsService(dir.Path))
        {
            svc.Settings.SeekStepSeconds = 17;
            svc.Settings.DefaultPlaybackRate = 1.75f;
            svc.Save();
        }

        using var reloaded = new SettingsService(dir.Path);
        Assert.Equal(17, reloaded.Settings.SeekStepSeconds);
        Assert.Equal(1.75f, reloaded.Settings.DefaultPlaybackRate);
    }

    [Fact]
    public void Save_LeavesNoLingeringTmpFile()
    {
        using var dir = new TempDir();
        using var svc = new SettingsService(dir.Path);
        svc.Save();

        Assert.True(System.IO.File.Exists(dir.File("settings.json")));
        Assert.False(System.IO.File.Exists(dir.File("settings.json.tmp")));
    }

    [Fact]
    public void Load_AppliesValidation_ToOutOfRangeValues()
    {
        using var dir = new TempDir();
        // Битый/злонамеренный файл с невалидным значением.
        System.IO.File.WriteAllText(dir.File("settings.json"), "{\"SeekStepSeconds\": -5}");

        using var svc = new SettingsService(dir.Path);
        Assert.Equal(5, svc.Settings.SeekStepSeconds); // подменено на дефолт
    }

    [Fact]
    public void Load_CorruptJson_FallsBackToDefaults()
    {
        using var dir = new TempDir();
        System.IO.File.WriteAllText(dir.File("settings.json"), "{ это не json ");

        var ex = Record.Exception(() =>
        {
            using var svc = new SettingsService(dir.Path);
            Assert.Equal(5, svc.Settings.SeekStepSeconds); // дефолты
        });
        Assert.Null(ex); // не бросает на битом файле
    }

    [Fact]
    public void Save_RaisesSettingsChanged()
    {
        using var dir = new TempDir();
        using var svc = new SettingsService(dir.Path);
        int raised = 0;
        svc.SettingsChanged += (_, _) => raised++;

        svc.Save();

        Assert.Equal(1, raised);
    }
}
