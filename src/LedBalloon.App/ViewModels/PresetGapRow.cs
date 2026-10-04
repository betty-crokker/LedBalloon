using LedBalloon.Core.Layout;

namespace LedBalloon.App.ViewModels;

/// <summary>
/// One preset that no longer covers the strip it is on, and the controller it is on.
/// <para>
/// The gap itself knows the preset and the two numbers; it does not know which box it came off,
/// and nothing can be done about it without that. Both repairs write to a controller.
/// </para>
/// </summary>
/// <param name="ControllerKey">Which controller holds it.</param>
/// <param name="Host">Where to write to.</param>
/// <param name="ControllerName">What that controller is called, for the sentence.</param>
/// <param name="Gap">What is wrong, in LEDs.</param>
public sealed record PresetGapRow(
    string ControllerKey,
    string Host,
    string ControllerName,
    PresetGap Gap)
{
    /// <summary>What the preset is called.</summary>
    public string Name => Gap.Preset.DisplayName;

    /// <summary>
    /// The problem, in the terms somebody standing in front of the house would use.
    /// </summary>
    /// <remarks>
    /// LEDs rather than segment bounds. "Covers 100 of 125" is a fact about a JSON document;
    /// "25 LEDs stay dark" is a fact about the porch.
    /// </remarks>
    public string Trouble =>
        $"{Name} on {ControllerName} was saved when the strip was shorter. " +
        $"{Gap.DarkLeds} LED{(Gap.DarkLeds == 1 ? "" : "s")} stay dark when it runs.";
}
