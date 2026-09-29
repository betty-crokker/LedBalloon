using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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
    /// Offered only when it is already the answer, and worded as a look with no name rather than as
    /// a mode — because that is what it is. The list is looks first and these two last: picking what
    /// a segment wears is the everyday thing, and the two ways of it wearing nothing are the
    /// exceptions to it.
    /// </remarks>
    public const string ItsOwn = "This scene only, unnamed";

    private readonly Action<string, string>? _chosen;
    private readonly Action<string>? _edit;
    private readonly bool _settling;

    public SceneSegmentRow(
        string segmentId,
        PresetDetail detail,
        IEnumerable<string>? choices,
        string? chosen,
        Action<string, string>? onChosen,
        Action<string>? onEdit = null)
    {
        SegmentId = segmentId;
        Detail = detail;
        _chosen = onChosen;
        _edit = onEdit;

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

    /// <summary>
    /// Opens this segment for editing, which the name on the card is a button for.
    /// </summary>
    /// <remarks>
    /// The photo could always do this and is a poor way to ask: it means finding the right few
    /// pixels of a traced line, and the shorter segments are barely a target at all - Porch is five
    /// LEDs. The name is already on screen, already says which segment it is, and cannot be missed.
    /// </remarks>
    [RelayCommand]
    private void Edit() => _edit?.Invoke(SegmentId);

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
