using LedBalloon.App.ViewModels;
using LedBalloon.Core.Layout;
using Xunit;

namespace LedBalloon.App.Tests;

/// <summary>
/// The view model reads and writes the preferences it is given, and no others.
/// <para>
/// It used to find them itself, in a field initialiser, which meant that merely constructing one read
/// a real file in the user's own folder and could write it back. Harmless on this machine and not the
/// sort of thing a test should be doing anywhere else. The seam is small - preferences are passed in,
/// and they remember which file they came from - but it is worth a test, because the failure it
/// prevents is silent and would only show up on somebody else's machine.
/// </para>
/// </summary>
[Collection(UiThreadCollection.Name)]
public class PreferencesStayInTheirOwnFileTests(UiThreadFixture ui) : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"ledballoon-app-tests-{Guid.NewGuid():N}.json");

    /// <summary>
    /// Opening with the given value and writing back to the given file, in one test on purpose.
    /// <para>
    /// The second half is what makes this a guard rather than a description. Whether the view model
    /// opened with the right value cannot fail on a machine whose real preferences happen to say the
    /// same thing - but a file that has to exist afterwards, in a folder named for this test, can only
    /// exist if the write went where it was told.
    /// </para>
    /// </summary>
    [Fact]
    public void The_view_model_reads_and_writes_the_preferences_it_was_given() => ui.Run(() =>
    {
        AppPreferences given = AppPreferences.Load(_path);
        given.LiveSync = false;

        var app = new MainViewModel(scanForControllers: false, given);

        Assert.False(app.LiveSync);

        // Opening writes, which is worth knowing rather than working around: the constructor assigns
        // through the property so that the hint beside the checkbox agrees with it, and assigning a
        // value that differs from the property's own default saves it straight back. Harmless - the
        // same value it just read - but it means the file exists by now, so it goes before the half of
        // this test that cares whether writing lands here.
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }

        app.LiveSync = true;

        Assert.True(given.LiveSync);

        // Written, and written here: a file that did not exist a moment ago and is not the usual one.
        Assert.True(File.Exists(_path), $"{_path} should have been written");
        Assert.True(AppPreferences.Load(_path).LiveSync);
        Assert.NotEqual(AppPreferences.DefaultPath, _path);

        return Task.CompletedTask;
    });

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
