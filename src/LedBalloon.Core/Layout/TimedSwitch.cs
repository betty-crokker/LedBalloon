using LedBalloon.Core.Models;

namespace LedBalloon.Core.Layout;

/// <summary>
/// The two presets the app keeps on every controller so that a timer can turn the house on or off.
/// <para>
/// A WLED timer stores a preset slot and nothing else. There is no "off" for it to point at, so
/// turning the lights off on a timetable means having a preset that is off and knowing which slot it
/// landed in - which is a thing about WLED, not a thing about the house, and not something to make
/// anybody learn in order to have the lights go off at bedtime.
/// </para>
/// <para>
/// So the app writes them itself, by name. <see cref="ScenePublisher"/> already chooses a slot by
/// name, replaces a preset it has written before rather than adding a second one, and asks before
/// overwriting something it did not write - all of which applies here unchanged. The names are the
/// identity: rename one on the controller and the next save writes a fresh pair, which is noisy but
/// not wrong.
/// </para>
/// <para>
/// "On" carries no brightness and no effect on purpose. It means the switch, not a look: the house
/// comes back to whatever it was last showing, the same as pressing the switch in the app. A scene
/// is what to use when the timer should decide the colors as well.
/// </para>
/// </summary>
public static class TimedSwitch
{
    /// <summary>What the on preset is called, on every controller.</summary>
    public const string OnName = "Everything on";

    /// <summary>What the off preset is called, on every controller.</summary>
    public const string OffName = "Everything off";

    /// <summary>The preset that turns a controller on, leaving everything else as it was.</summary>
    public static WledPreset On() => new() { Name = OnName, On = true };

    /// <summary>The preset that turns a controller off.</summary>
    public static WledPreset Off() => new() { Name = OffName, On = false };

    /// <summary>The preset for one end of the switch.</summary>
    public static WledPreset For(bool on) => on ? On() : Off();

    /// <summary>What the preset in a slot is called, when that slot holds one of these.</summary>
    public static string NameFor(bool on) => on ? OnName : OffName;

    /// <summary>
    /// Whether a preset name is one of these, so a timetable read back off a controller can be
    /// recognised as a switch rather than shown as a scene nobody chose.
    /// </summary>
    public static bool? SwitchIn(string? presetName) => presetName switch
    {
        OnName => true,
        OffName => false,
        _ => null,
    };
}
