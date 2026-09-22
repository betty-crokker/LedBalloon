using System.Collections.ObjectModel;
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

    /// <summary>True for the run being edited, so the list can show which one that is.</summary>
    [ObservableProperty] private bool _isSelected;

    public SegmentRow(Segment segment, string controllerName, MainViewModel? owner = null)
    {
        Segment = segment;
        _controllerName = controllerName;
        Owner = owner;

        Segment.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Segment.Count) or nameof(Segment.Start) or nameof(Segment.StopExclusive))
            {
                OnPropertyChanged(nameof(Placement));
            }
        };
    }

    public Segment Segment { get; }

    /// <summary>
    /// Where this segment sits on the wire, as one string.
    /// <para>
    /// One string rather than the numbers stitched together from several bound
    /// <c>&lt;Run&gt;</c> inlines in the row template. Inlines are built once and do not re-bind, so
    /// the moment a length was corrected - or a neighbour was pushed along by one - the row went
    /// blank and stayed blank until the whole list was rebuilt.
    /// </para>
    /// </summary>
    public string Placement =>
        $"{Segment.Count} LEDs · LED {Segment.Start} to {Segment.StopExclusive}";

    /// <summary>
    /// The view model, reachable from the row itself.
    /// <para>
    /// Because the editor is in a popup now. A flyout's contents are not in the window's visual
    /// tree, so the usual trick of walking up to the window to find the commands does not resolve
    /// there - it binds to nothing and the buttons quietly do nothing. Everything the popup needs
    /// has to hang off the thing the popup is about.
    /// </para>
    /// </summary>
    public MainViewModel? Owner { get; init; }
}

/// <summary>
/// What one controller has described, and what it has not.
/// <para>
/// This is the answer to "my controller has two outputs, where do I say that": it shows each
/// controller with how many of its LEDs are spoken for, and offers to bring in the rest.
/// </para>
/// </summary>
public sealed partial class ControllerCoverage(
    string Key,
    string Name,
    int SegmentCount,
    int AssignedLeds,
    int TotalLeds) : ObservableObject
{
    public string Key { get; } = Key;

    public string Name { get; } = Name;

    /// <summary>
    /// Where it answers, for the card to show. The panel used to list the found controllers a
    /// second time at the foot of it purely to carry this one string.
    /// </summary>
    public string? Host { get; init; }

    /// <summary>Whether it is answering right now, for the dot beside the name.</summary>
    public bool IsConnected { get; init; }

    public int SegmentCount { get; } = SegmentCount;

    public int AssignedLeds { get; } = AssignedLeds;

    public int TotalLeds { get; } = TotalLeds;

    /// <summary>The view model, for the same reason <see cref="SegmentRow.Owner"/> exists.</summary>
    public MainViewModel? Owner { get; init; }

    /// <summary>
    /// The runs plugged into this controller.
    /// <para>
    /// Each controller keeps its own list so they can all be on screen at once. A single list
    /// filtered to the selection would show the same runs under every controller, which is worse
    /// than the heading it replaced.
    /// </para>
    /// </summary>
    public ObservableCollection<SegmentRow> Runs { get; } = [];

    public bool HasRuns => Runs.Count > 0;

    /// <summary>Call after refilling <see cref="Runs"/>, which is a collection and says nothing itself.</summary>
    public void RunsChanged() => OnPropertyChanged(nameof(HasRuns));

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
