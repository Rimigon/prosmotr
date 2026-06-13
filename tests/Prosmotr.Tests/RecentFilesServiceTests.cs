using Prosmotr.Models;
using Prosmotr.Services;
using Prosmotr.Services.Abstractions;
using Xunit;

namespace Prosmotr.Tests;

/// <summary>Недавние файлы: дедупликация, порядок (новые сверху), лимит, атомарная подмена списка.</summary>
public sealed class RecentFilesServiceTests
{
    /// <summary>Минимальный фейк настроек в памяти.</summary>
    private sealed class FakeSettings : ISettingsService
    {
        public AppSettings Settings { get; } = new();
        public event EventHandler? SettingsChanged;
        public int SaveCalls;
        public int SaveDebouncedCalls;
        public void Save() { SaveCalls++; SettingsChanged?.Invoke(this, EventArgs.Empty); }
        public void SaveDebounced() => SaveDebouncedCalls++;
    }

    [Fact]
    public void Add_PutsNewestFirst()
    {
        var settings = new FakeSettings();
        var svc = new RecentFilesService(settings);

        svc.Add(@"C:\a.jpg", false);
        svc.Add(@"C:\b.jpg", false);

        Assert.Equal(@"C:\b.jpg", svc.Items[0].Path);
        Assert.Equal(@"C:\a.jpg", svc.Items[1].Path);
    }

    [Fact]
    public void Add_DeduplicatesByPath_CaseInsensitive()
    {
        var settings = new FakeSettings();
        var svc = new RecentFilesService(settings);

        svc.Add(@"C:\a.jpg", false);
        svc.Add(@"C:\A.JPG", false); // тот же файл в другом регистре

        Assert.Single(svc.Items);
        Assert.Equal(@"C:\A.JPG", svc.Items[0].Path); // самая свежая запись побеждает
    }

    [Fact]
    public void Add_CapsAtFifteenItems()
    {
        var settings = new FakeSettings();
        var svc = new RecentFilesService(settings);

        for (int i = 0; i < 20; i++)
            svc.Add($@"C:\file{i}.jpg", false);

        Assert.Equal(15, svc.Items.Count);
        // Самый свежий (file19) — в списке, самый старый (file0) — вытеснен.
        Assert.Contains(svc.Items, r => r.Path == @"C:\file19.jpg");
        Assert.DoesNotContain(svc.Items, r => r.Path == @"C:\file0.jpg");
    }

    [Fact]
    public void Add_ReplacesListReference_NotMutatesInPlace()
    {
        // Регрессионный тест на атомарную подмену: ссылка на список должна меняться,
        // чтобы фоновая сериализация настроек не ловила список в момент мутации.
        var settings = new FakeSettings();
        var svc = new RecentFilesService(settings);
        var before = settings.Settings.RecentFiles;

        svc.Add(@"C:\a.jpg", false);

        Assert.NotSame(before, settings.Settings.RecentFiles);
    }

    [Fact]
    public void Clear_EmptiesAndReplacesReference()
    {
        var settings = new FakeSettings();
        var svc = new RecentFilesService(settings);
        svc.Add(@"C:\a.jpg", false);
        var before = settings.Settings.RecentFiles;

        svc.Clear();

        Assert.Empty(svc.Items);
        Assert.NotSame(before, settings.Settings.RecentFiles);
    }

    [Fact]
    public void Add_RaisesChangedEvent()
    {
        var settings = new FakeSettings();
        var svc = new RecentFilesService(settings);
        int raised = 0;
        svc.Changed += (_, _) => raised++;

        svc.Add(@"C:\a.jpg", false);

        Assert.Equal(1, raised);
        Assert.Equal(1, settings.SaveDebouncedCalls);
    }
}
