using LedBalloon.Core.Layout;
using LedBalloon.Core.Models;
using Xunit;

namespace LedBalloon.Core.Tests;

/// <summary>
/// The presets behind a whole-house on/off timer.
/// <para>
/// A WLED timer stores a preset slot and nothing else, so "off at half past ten" has to be a preset
/// that is off, in a slot the timer can name. Making somebody build that themselves is the thing
/// this exists to avoid, so the app writes the pair itself and keeps the names as their identity.
/// </para>
/// </summary>
public class TurningTheHouseOnAndOffOnATimerTests
{
    [Fact]
    public void The_off_preset_is_off_and_says_nothing_else()
    {
        WledPreset off = TimedSwitch.Off();

        Assert.Equal(TimedSwitch.OffName, off.Name);
        Assert.False(off.On);

        // No brightness, because a preset that carries one turns the lights on when it is applied -
        // WLED reads a bri as a request to come on - which is the opposite of what this is for.
        Assert.Null(off.Brightness);
    }

    /// <summary>
    /// On means the switch, not a look.
    /// </summary>
    /// <remarks>
    /// Carrying a brightness or an effect would make this a scene, and a scene that quietly
    /// overrides whatever the house was set to the evening before. The house comes back to what it
    /// was last showing, which is what pressing the switch in the app does.
    /// </remarks>
    [Fact]
    public void The_on_preset_restores_rather_than_decides()
    {
        WledPreset on = TimedSwitch.On();

        Assert.Equal(TimedSwitch.OnName, on.Name);
        Assert.True(on.On);
        Assert.Null(on.Brightness);
        Assert.Null(on.Playlist);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Each_end_of_the_switch_is_recognised_by_the_name_it_was_written_under(bool on)
    {
        WledPreset preset = TimedSwitch.For(on);

        Assert.Equal(on, TimedSwitch.SwitchIn(preset.Name));
    }

    /// <summary>
    /// A scene must not be mistaken for the switch, however it is named.
    /// </summary>
    [Fact]
    public void Anything_else_in_the_slot_is_a_scene()
    {
        Assert.Null(TimedSwitch.SwitchIn("Porchline blue"));
        Assert.Null(TimedSwitch.SwitchIn("Off"));
        Assert.Null(TimedSwitch.SwitchIn(null));
        Assert.Null(TimedSwitch.SwitchIn(string.Empty));
    }

    /// <summary>
    /// The two ends have to be different presets, or one timer would undo the other.
    /// </summary>
    [Fact]
    public void The_two_ends_are_not_the_same_preset()
    {
        Assert.NotEqual(TimedSwitch.OnName, TimedSwitch.OffName);
        Assert.NotEqual(TimedSwitch.On().On, TimedSwitch.Off().On);
    }
}
