using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LedBalloon.Core;
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
    /// How long this run is.
    /// <para>
    /// It used to carry the LED range too - "5 LEDs, LED 20 to 25". Those numbers are worked out
    /// now, and nobody types or checks them, so they were two thirds of the line saying nothing.
    /// </para>
    /// <para>
    /// One bound string rather than several <c>&lt;Run&gt;</c> inlines. Inlines are built once and
    /// do not re-bind, so the moment a length was corrected the row went blank and stayed blank.
    /// </para>
    /// </summary>
    public string Placement => $"{Segment.Count} LEDs";

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

    /// <summary>Its LED outputs as the controller itself reports them, in its own order.</summary>
    public IReadOnlyList<LedBus> Wiring { get; init; } = [];

    public int SegmentCount { get; } = SegmentCount;

    public int AssignedLeds { get; } = AssignedLeds;

    public int TotalLeds { get; } = TotalLeds;

    /// <summary>The view model, for the same reason <see cref="SegmentRow.Owner"/> exists.</summary>
    public MainViewModel? Owner { get; init; }

    /// <summary>
    /// The runs plugged into this controller, grouped by the output they are plugged into.
    /// <para>
    /// Grouped because the order within an output is now something you set, and "move earlier" is
    /// unusable when you cannot see what it is moving inside of. The group is also the only honest
    /// place to show GPIO 16 against GPIO 2: it is a property of the output, not of the run.
    /// </para>
    /// </summary>
    public ObservableCollection<OutputGroup> Outputs { get; } = [];

    public bool HasRuns => Outputs.Any(o => o.Runs.Count > 0);

    /// <summary>Call after refilling <see cref="Outputs"/>, which says nothing itself.</summary>
    public void RunsChanged() => OnPropertyChanged(nameof(HasRuns));

    /// <summary>
    /// What is plugged in. No longer "all 356 LEDs described": the controller's LED count is the
    /// sum of the runs now, and saving writes it, so that sentence had become a tautology dressed
    /// up as reassurance - it could never say anything but "all".
    /// </summary>
    public string Summary
    {
        get
        {
            int leds = Outputs.Sum(o => o.Length);
            int runs = Outputs.Sum(o => o.Runs.Count);

            return runs == 0
                ? "Nothing plugged in yet"
                : $"{runs} segment(s) · {leds} LEDs · {Outputs.Count} output(s)";
        }
    }
}

/// <summary>One of a controller's LED outputs, with the runs chained off it in order.</summary>
public sealed partial class OutputGroup(int Number, string Pins) : ObservableObject
{
    public int Number { get; } = Number;

    /// <summary>The GPIO pins, as the controller reports them. Empty when it has not said.</summary>
    public string Pins { get; } = Pins;

    public ObservableCollection<SegmentRow> Runs { get; } = [];

    public string Header => Pins.Length == 0
        ? $"Output {Number}"
        : $"Output {Number}  ·  GPIO {Pins}";

    /// <summary>How long this output is, which is the runs plugged into it added up.</summary>
    public int Length => Runs.Sum(r => r.Segment.Count);

    public string Summary => Runs.Count == 0
        ? "nothing on it"
        : $"{Length} LEDs";

    public void RunsChanged()
    {
        OnPropertyChanged(nameof(Length));
        OnPropertyChanged(nameof(Summary));
    }
}
