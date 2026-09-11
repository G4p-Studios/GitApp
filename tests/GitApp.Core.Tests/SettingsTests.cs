using GitApp.Services;

namespace GitApp.Core.Tests;

public class SettingsTests
{
    [Fact]
    public void DefaultsMatchGitsOwnContext()
    {
        var settings = new AppSettings();

        Assert.Equal(3, settings.DiffContextLines);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(3, 3)]
    [InlineData(9999, 50)]
    public void ContextIsClampedToSomethingPresentable(int given, int expected)
    {
        var clamped = new AppSettings { DiffContextLines = given }.Clamped();

        Assert.Equal(expected, clamped.DiffContextLines);
    }

    [Fact]
    public async Task MissingFileLeavesTheDefaultsAndNoError()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gitapp-{Guid.NewGuid():N}.json");
        var store = new SettingsStore(path);

        await store.LoadAsync();

        Assert.Null(store.LoadError);
        Assert.Equal(3, store.Settings.DiffContextLines);
    }

    [Fact]
    public async Task CorruptFileIsReportedRatherThanSwallowed()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gitapp-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, "{ not json");

        try
        {
            var store = new SettingsStore(path);
            await store.LoadAsync();

            Assert.NotNull(store.LoadError);
            Assert.Equal(3, store.Settings.DiffContextLines);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SavedSettingsSurviveARoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gitapp-{Guid.NewGuid():N}.json");

        try
        {
            var write = new SettingsStore(path);
            write.Settings.DiffContextLines = 12;
            await write.SaveAsync();

            var read = new SettingsStore(path);
            await read.LoadAsync();

            Assert.Equal(12, read.Settings.DiffContextLines);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
