using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LedBalloon.App.ViewModels;

/// <summary>
/// One segment's card in the scene editor: what the scene does about it, and the choice of what it
/// should do instead.
/// <para>
/// The three choices are the whole point. A scene can show something on a segment, switch it off, or
/// say nothing about it — and the difference between the last two is invisible on a dark house and
/// total the rest of the time, so it has to be something you pick rather than something you infer.
/// </para>
/// </summary>
public sealed partial class SceneSegmentRow : ObservableObject
{
    /// <summary>The choice that means the scene leaves this segment alone.</summary>
    public const string NotIncluded = "Not included";

    /// <summary>The choice that means the scene switches this segment off.</summary>
    public const string Off = "Off";

    /// <summary>
    /// The choice standing for an appearance this scene holds on its own, rather than by name.
    /// </summary>
    /// <remarks>
    /// Offered only when it is already the answer. Most segments in most scenes are one-offs, and
    /// there is nothing to pick here — "give this segment settings of its own" is done by editing
    /// it, not by choosing it from a list. It is in the list so that the list can show what the
    /// segment is doing now without pretending it is a look.
    /// </remarks>
    public const string ItsOwn = "Its own settings";

    private readonly Action<string, string>? _chosen;
    private readonly bool _settling;

    public SceneSegmentRow(
        string segmentId,
        PresetDetail detail,
        IEnumerable<string>? choices,
        string? chosen,
        Action<string, string>? onChosen)
    {
        SegmentId = segmentId;
        Detail = detail;
        _chosen = onChosen;

        foreach (string choice in choices ?? [])
        {
            Choices.Add(choice);
        }

        _settling = true;
        Choice = chosen;
        _settling = false;
    }

    public string SegmentId { get; }

    public PresetDetail Detail { get; }

    public ObservableCollection<string> Choices { get; } = [];

    /// <summary>False while looking at the house, which is not a scene and has nothing to choose.</summary>
    public bool Choosable => Choices.Count > 0;

    [ObservableProperty] private string? _choice;

    partial void OnChoiceChanged(string? value)
    {
        if (_settling || value is null)
        {
            return;
        }

        _chosen?.Invoke(SegmentId, value);
    }
}
