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

    /// <summary>
    /// Preferences remember the file they came from, and saving with nothing to say puts them back
    /// there rather than in the usual place.
    /// <para>
    /// Which is what lets anything be handed preferences of its own - a test, most of all, since the
    /// usual place is a real file in whoever's folder is running it. A first run counts too: there is
    /// nothing to read, and the defaults that come back still have to know where they were looked for
    /// or the first save goes somewhere else.
    /// </para>
    /// </summary>
    [Fact]
    public void Preferences_are_saved_back_to_the_file_they_came_from()
    {
        AppPreferences fresh = AppPreferences.Load(_path);

        Assert.Equal(_path, fresh.StoredAt);
        Assert.False(File.Exists(_path));

        fresh.LiveSync = false;
        fresh.Save();

        Assert.True(File.Exists(_path));
        Assert.False(AppPreferences.Load(_path).LiveSync);

        // And the default file is left alone, which is the whole point of carrying the path.
        Assert.NotEqual(AppPreferences.DefaultPath, fresh.StoredAt);
    }

    /// <summary>Preferences nobody gave a file to still save where they always did.</summary>
    [Fact]
    public void Preferences_made_from_nothing_still_know_the_usual_place()
    {
        Assert.Equal(AppPreferences.DefaultPath, new AppPreferences().StoredAt);
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
