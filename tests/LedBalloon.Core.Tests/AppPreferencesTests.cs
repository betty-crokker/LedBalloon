using LedBalloon.Core.Layout;
using Xunit;

namespace LedBalloon.Core.Tests;

/// <summary>
/// The one file this app keeps on the PC that is not a cached photo. It holds nothing about the
/// house, so losing it costs a checkbox position - which is why none of this is allowed to throw.
/// </summary>
public class AppPreferencesTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(),
        $"ledballoon-prefs-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    [Fact]
    public void Sync_starts_on_when_nothing_has_been_saved()
    {
        Assert.True(AppPreferences.Load(_path).LiveSync);
    }

    [Fact]
    public void Sync_comes_back_off_once_it_has_been_turned_off()
    {
        new AppPreferences { LiveSync = false }.Save(_path);

        Assert.False(AppPreferences.Load(_path).LiveSync);
    }

    [Fact]
    public void Turning_it_back_on_sticks_too()
    {
        new AppPreferences { LiveSync = false }.Save(_path);
        new AppPreferences { LiveSync = true }.Save(_path);

        Assert.True(AppPreferences.Load(_path).LiveSync);
    }

    [Fact]
    public void A_damaged_file_does_not_stop_the_app_starting()
    {
        File.WriteAllText(_path, "{ this is not json");

        Assert.True(AppPreferences.Load(_path).LiveSync);
    }

    [Fact]
    public void Saving_creates_the_folder_it_needs()
    {
        string nested = Path.Combine(
            Path.GetTempPath(),
            $"ledballoon-{Guid.NewGuid():N}",
            "deeper",
            "preferences.json");

        try
        {
            new AppPreferences { LiveSync = false }.Save(nested);

            Assert.True(File.Exists(nested));
            Assert.False(AppPreferences.Load(nested).LiveSync);
        }
        finally
        {
            if (Path.GetDirectoryName(Path.GetDirectoryName(nested)) is { Length: > 0 } root &&
                Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
