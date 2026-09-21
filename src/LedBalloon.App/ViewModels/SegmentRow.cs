using CommunityToolkit.Mvvm.ComponentModel;
using LedBalloon.Core.Layout;

namespace LedBalloon.App.ViewModels;

/// <summary>
/// One row in the segment list: the run itself, plus the name of the controller driving it.
/// <para>
/// A segment knows its controller's MAC, which is the right thing to store and the wrong thing to
/// show. The list needs "Front of house", and a run has no way to look that up on its own.
/// </para>
/// </summary>
public sealed partial class SegmentRow : ObservableObject
{
    [ObservableProperty] private string _controllerName;

    public SegmentRow(Segment segment, string controllerName)
    {
        Segment = segment;
        _controllerName = controllerName;
    }

    public Segment Segment { get; }
}

/// <summary>
/// What one controller has described, and what it has not.
/// <para>
/// This is the answer to "my controller has two outputs, where do I say that": it shows each
/// controller with how many of its LEDs are spoken for, and offers to bring in the rest.
/// </para>
/// </summary>
public sealed record ControllerCoverage(
    string Key,
    string Name,
    int SegmentCount,
    int AssignedLeds,
    int TotalLeds)
{
    public int UnassignedLeds => Math.Max(0, TotalLeds - AssignedLeds);

    public bool NeedsAttention => SegmentCount == 0 || UnassignedLeds > 0;

    public string Summary => SegmentCount switch
    {
        0 => $"{TotalLeds} LEDs, none described yet",
        _ when UnassignedLeds > 0 =>
            $"{SegmentCount} segment(s) · {UnassignedLeds} of {TotalLeds} LEDs not described",
        _ => $"{SegmentCount} segment(s) · all {TotalLeds} LEDs described",
    };
}
