using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;

namespace LedBalloon.App.ViewModels;

/// <summary>
/// Something the house can be told to come back to after a power cut, named rather than numbered.
/// <para>
/// The same reasoning as <see cref="PresetChoice"/>, for the same reason: a controller stores a
/// boot preset as a slot, and the same scene sits in a different slot on each box. The name is the
/// identity here, and the save works out the number for each controller in turn.
/// </para>
/// <para>
/// Two of these name no scene at all. Coming back dark and coming back lit are the controller's own
/// switch - <c>def.on</c> - and they need no preset to be stored anywhere, which also makes them the
/// only two choices that are certain to work on a controller holding nothing this app put there.
/// </para>
/// </summary>
/// <param name="Label">What the picker reads.</param>
/// <param name="LightsUp">Whether the strip comes up lit.</param>
/// <param name="Scene">The scene it comes back to, or null for the plain switch.</param>
public sealed record BootChoice(string Label, bool LightsUp, string? Scene = null)
{
    /// <summary>A band of what the scene puts on the house, for telling one name from another.</summary>
    public IBrush? Swatch { get; init; }

    public bool HasSwatch => Swatch is not null;

    public override string ToString() => Label;

    /// <summary>
    /// Dark, which is what a house with nothing set up comes back to, and the one choice that is
    /// always safe to pick.
    /// </summary>
    public static BootChoice Dark { get; } = new("Come back dark", false);

    /// <summary>
    /// Lit, without naming a look.
    /// </summary>
    /// <remarks>
    /// Worth its own line because it is what people mean by "come back on", and worth the words
    /// after the comma because it is not the same as coming back to a scene: the controller keeps
    /// the segment setup it last persisted, which may be a scene somebody applied months ago or
    /// whatever was being fiddled with when the power went.
    /// </remarks>
    public static BootChoice Lit { get; } = new("Come back lit, however it was last set up", true);

    /// <summary>The two switch choices, then the scenes, which is the order they read in.</summary>
    public static IReadOnlyList<BootChoice> Offered(
        IEnumerable<(string Name, IBrush? Swatch)> scenes) =>
    [
        Dark,
        Lit,
        .. scenes.Select(scene =>
            new BootChoice($"Come back to {scene.Name}", true, scene.Name) { Swatch = scene.Swatch }),
    ];
}
