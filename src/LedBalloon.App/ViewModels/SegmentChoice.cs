using Avalonia.Media;

namespace LedBalloon.App.ViewModels;

/// <summary>What a scene can be told to do about one segment.</summary>
public enum SegmentChoiceKind
{
    /// <summary>Wear a named look, which other segments and other scenes may wear too.</summary>
    Look,

    /// <summary>Wear the appearance this scene holds for it, which has no name.</summary>
    Unnamed,

    /// <summary>Be left alone.</summary>
    NotIncluded,

    /// <summary>Be switched off.</summary>
    Off,
}

/// <summary>
/// One row of a segment's picker: what it would look like, rather than what it is called.
/// <para>
/// The rows carry their own swatches, effect and speed because that is the question being asked —
/// what should this segment show? A list of names answers it only for someone who remembers what
/// each name means, and the description was previously repeated underneath the picker for exactly
/// that reason, where it described the one row that happened to be selected and none of the others.
/// </para>
/// </summary>
public sealed class SegmentChoice
{
    public required SegmentChoiceKind Kind { get; init; }

    /// <summary>The look this row picks, when it picks one.</summary>
    public string? LookId { get; init; }

    /// <summary>The look's name, or null for a row that is not a named look.</summary>
    public string? Name { get; init; }

    /// <summary>The effect and how it moves, or the whole of the row for the two silences.</summary>
    public required string Description { get; init; }

    /// <summary>True for a row that shows something, so has colors worth drawing.</summary>
    public bool HasAppearance { get; init; }

    public IBrush Primary { get; init; } = Brushes.Transparent;

    public IBrush Secondary { get; init; } = Brushes.Transparent;

    public bool HasSecondary { get; init; }

    public bool HasName => Name is { Length: > 0 };

    public override string ToString() => HasName ? $"{Name} — {Description}" : Description;
}
