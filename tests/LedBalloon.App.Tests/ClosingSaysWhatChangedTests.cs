using LedBalloon.App.ViewModels;
using LedBalloon.Core.Layout;
using Xunit;

namespace LedBalloon.App.Tests;

/// <summary>
/// Closing with a changed scene used to be described as a change to the layout, which is the one
/// thing it is not. There is only ever one save and one prompt — a scene lives in the project and
/// travels to the controllers with it — so the fix was for the prompt to name what was changed
/// rather than to grow a second prompt beside it.
/// <para>
/// Kept apart from the scene open on screen on purpose. That one is forgotten the moment a different
/// scene is picked, so a prompt built on it would stay silent about a scene changed and then clicked
/// away from — which is the case where being told matters most, because there is nothing on screen
/// to remind you of it.
/// </para>
/// </summary>
public class ClosingSaysWhatChangedTests
{
    private static Scene Named(string name) => new() { Name = name };

    private static string Names(IEnumerable<Scene> scenes, params Scene[] edited) =>
        MainViewModel.EditedSceneNames(scenes, edited.Select(s => s.Id).ToHashSet());

    [Fact]
    public void Nothing_changed_is_said_with_silence()
    {
        // The caller falls back to the general wording on an empty string, so this is the branch
        // that decides a layout-only change still reads as a layout change.
        Assert.Equal(string.Empty, Names([Named("Twinkle both")]));
    }

    [Fact]
    public void One_changed_scene_is_named()
    {
        Scene twinkle = Named("Twinkle both");

        Assert.Equal("'Twinkle both'", Names([twinkle], twinkle));
    }

    [Fact]
    public void Two_are_joined_with_and_rather_than_a_comma()
    {
        Scene twinkle = Named("Twinkle both");
        Scene candles = Named("Candles");

        Assert.Equal("'Twinkle both' and 'Candles'", Names([twinkle, candles], twinkle, candles));
    }

    [Fact]
    public void More_than_two_are_a_list_ending_in_and()
    {
        Scene a = Named("Twinkle both");
        Scene b = Named("Candles");
        Scene c = Named("Fourth of July");

        Assert.Equal(
            "'Twinkle both', 'Candles' and 'Fourth of July'", Names([a, b, c], a, b, c));
    }

    [Fact]
    public void Only_the_ones_that_were_changed_are_named()
    {
        Scene twinkle = Named("Twinkle both");
        Scene untouched = Named("Candles");

        Assert.Equal("'Twinkle both'", Names([twinkle, untouched], twinkle));
    }

    [Fact]
    public void A_scene_changed_and_then_removed_is_not_offered_for_saving()
    {
        // Changing a scene and then deleting it leaves its id behind in the record of what was
        // touched. Naming it on the way out would offer to save something that no longer exists,
        // and the only honest answer to "save 'Candles'?" for a scene that has been removed is not
        // to ask.
        Scene kept = Named("Twinkle both");
        Scene removed = Named("Candles");

        Assert.Equal("'Twinkle both'", Names([kept], kept, removed));
    }

    [Fact]
    public void Removing_the_only_changed_scene_takes_the_wording_back_to_silence()
    {
        Scene removed = Named("Candles");

        Assert.Equal(string.Empty, Names([], removed));
    }

    [Fact]
    public void The_name_is_read_at_closing_time_rather_than_at_editing_time()
    {
        // Scenes are recorded by id for this reason: renaming one is itself a change, and being
        // asked about a name the scene no longer has would be asking about nothing recognizable.
        Scene scene = Named("Twinkle both");
        HashSet<string> edited = [scene.Id];

        scene.Name = "Twinkle everything";

        Assert.Equal("'Twinkle everything'", MainViewModel.EditedSceneNames([scene], edited));
    }
}
