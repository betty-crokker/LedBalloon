using System.Text.Json.Serialization;

namespace LedBalloon.Core.Models;

/// <summary>
/// One WLED segment. Every property is nullable on purpose: null means "not part of this patch",
/// and the serializer omits it, which is exactly how the partial-update API expects to be driven.
/// </summary>
public sealed class WledSegment
{
    [JsonPropertyName("id")] public int? Id { get; set; }
    [JsonPropertyName("n")] public string? Name { get; set; }

    [JsonPropertyName("start")] public int? Start { get; set; }
    [JsonPropertyName("stop")] public int? Stop { get; set; }
    [JsonPropertyName("len")] public int? Length { get; set; }
    [JsonPropertyName("grp")] public int? Grouping { get; set; }
    [JsonPropertyName("spc")] public int? Spacing { get; set; }
    [JsonPropertyName("of")] public int? Offset { get; set; }

    [JsonPropertyName("on")] public bool? On { get; set; }
    [JsonPropertyName("frz")] public bool? Frozen { get; set; }
    [JsonPropertyName("bri")] public byte? Brightness { get; set; }
    [JsonPropertyName("cct")] public int? ColorTemperature { get; set; }

    /// <summary>Up to three color slots (primary, secondary, tertiary), each 3 or 4 channels.</summary>
    /// <summary>
    /// The segment's three color slots, as WLED's <c>col</c> array. A null entry means "leave this
    /// slot alone" - see <see cref="SetColorSlot"/>.
    /// </summary>
    [JsonPropertyName("col")] public int[]?[]? Colors { get; set; }

    [JsonPropertyName("fx")] public int? Effect { get; set; }
    [JsonPropertyName("sx")] public byte? Speed { get; set; }
    [JsonPropertyName("ix")] public byte? Intensity { get; set; }
    [JsonPropertyName("pal")] public int? Palette { get; set; }

    [JsonPropertyName("sel")] public bool? Selected { get; set; }
    [JsonPropertyName("rev")] public bool? Reverse { get; set; }
    [JsonPropertyName("mi")] public bool? Mirror { get; set; }

    // Effect option checkboxes, named o1/o2/o3 in the API.
    [JsonPropertyName("o1")] public bool? Option1 { get; set; }
    [JsonPropertyName("o2")] public bool? Option2 { get; set; }
    [JsonPropertyName("o3")] public bool? Option3 { get; set; }

    [JsonPropertyName("si")] public int? SoundSimulation { get; set; }

    // Effect custom sliders. Modern effects lean on these heavily; without them a captured look
    // loses most of its character. Confirmed present on 0.15.3.
    [JsonPropertyName("c1")] public byte? Custom1 { get; set; }
    [JsonPropertyName("c2")] public byte? Custom2 { get; set; }
    [JsonPropertyName("c3")] public byte? Custom3 { get; set; }

    /// <summary>Segment set (0-3), used to group segments for the UI.</summary>
    [JsonPropertyName("set")] public int? Set { get; set; }

    /// <summary>How a 1D effect is expanded onto a 2D matrix.</summary>
    [JsonPropertyName("m12")] public int? Expand1Dto2D { get; set; }

    /// <summary>
    /// True for the placeholder entries WLED pads a preset with, one per unused segment slot.
    /// A 32-segment controller writes 30 of these into every preset; they are not real segments.
    /// </summary>
    [JsonIgnore]
    public bool IsPlaceholder => Stop == 0 && Start is null or 0 && Colors is null && Effect is null;

    [JsonIgnore]
    public RgbColor PrimaryColor
    {
        get => RgbColor.FromWledArray(Colors is { Length: > 0 } ? Colors[0] : null);
        set => SetColorSlot(0, value);
    }

    [JsonIgnore]
    public RgbColor SecondaryColor
    {
        get => RgbColor.FromWledArray(Colors is { Length: > 1 } ? Colors[1] : null);
        set => SetColorSlot(1, value);
    }

    /// <summary>
    /// Writes one color slot, saying nothing about the others.
    /// <para>
    /// WLED does accept a sparse array, and null is how it is spelled: deserializeSegment walks all
    /// three slots, and an entry that is not an array, an object, a hex string or a Kelvin number
    /// is skipped by its <c>continue</c>. Back-filling the lower slots with black instead - which
    /// this used to do - meant that setting the background sent <c>[[0,0,0],[r,g,b]]</c> and really
    /// did turn the primary off, on the controller and not only on screen.
    /// </para>
    /// <para>
    /// Only below, because the array stops at the slot being written, so setting the primary
    /// dropped the background instead of blacking it: on the wire that is harmless, since WLED
    /// leaves a slot it was not told about, but <see cref="MergeFrom"/> laid the short array over
    /// the long one and the editor read the missing slots back as black.
    /// </para>
    /// </summary>
    public void SetColorSlot(int slot, RgbColor color)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(slot, 2);

        int[]?[] existing = Colors ?? [];
        int[]?[] next = new int[]?[Math.Max(existing.Length, slot + 1)];

        for (int i = 0; i < next.Length; i++)
        {
            next[i] = i < existing.Length ? existing[i] : null;
        }

        next[slot] = color.ToWledArray();
        Colors = next;
    }

    /// <summary>
    /// An independent copy. Lets a state be drawn with pending changes laid over it without those
    /// changes reaching the live state they were copied from.
    /// </summary>
    public WledSegment Clone()
    {
        var copy = (WledSegment)MemberwiseClone();

        // The color slots are the only reference type here, and merging replaces the whole array,
        // so copying one level deep is enough.
        copy.Colors = Colors?.Select(slot => slot?.ToArray()).ToArray();

        return copy;
    }

    /// <summary>Copies every non-null field of <paramref name="newer"/> over this segment.</summary>
    public void MergeFrom(WledSegment newer)
    {
        ArgumentNullException.ThrowIfNull(newer);

        Id ??= newer.Id;
        Name = newer.Name ?? Name;
        Start = newer.Start ?? Start;
        Stop = newer.Stop ?? Stop;
        Length = newer.Length ?? Length;
        Grouping = newer.Grouping ?? Grouping;
        Spacing = newer.Spacing ?? Spacing;
        Offset = newer.Offset ?? Offset;
        On = newer.On ?? On;
        Frozen = newer.Frozen ?? Frozen;
        Brightness = newer.Brightness ?? Brightness;
        ColorTemperature = newer.ColorTemperature ?? ColorTemperature;
        Colors = MergedColors(Colors, newer.Colors);
        Effect = newer.Effect ?? Effect;
        Speed = newer.Speed ?? Speed;
        Intensity = newer.Intensity ?? Intensity;
        Palette = newer.Palette ?? Palette;
        Selected = newer.Selected ?? Selected;
        Reverse = newer.Reverse ?? Reverse;
        Mirror = newer.Mirror ?? Mirror;
        Option1 = newer.Option1 ?? Option1;
        Option2 = newer.Option2 ?? Option2;
        Option3 = newer.Option3 ?? Option3;
        SoundSimulation = newer.SoundSimulation ?? SoundSimulation;
        Custom1 = newer.Custom1 ?? Custom1;
        Custom2 = newer.Custom2 ?? Custom2;
        Custom3 = newer.Custom3 ?? Custom3;
        Set = newer.Set ?? Set;
        Expand1Dto2D = newer.Expand1Dto2D ?? Expand1Dto2D;
    }

    /// <summary>
    /// Lays one segment's color slots over another's, slot by slot rather than wholesale.
    /// <para>
    /// Slot by slot because a patch that sets the background carries nothing in the primary's
    /// place, and replacing the whole array with it blanked the primary in the editor while the
    /// controller still had it. A null slot is "unchanged" here for the same reason every other
    /// field's null is.
    /// </para>
    /// </summary>
    private static int[]?[]? MergedColors(int[]?[]? older, int[]?[]? newer)
    {
        if (newer is null || older is null)
        {
            return newer ?? older;
        }

        int[]?[] merged = new int[]?[Math.Max(older.Length, newer.Length)];

        for (int i = 0; i < merged.Length; i++)
        {
            merged[i] = (i < newer.Length ? newer[i] : null)
                ?? (i < older.Length ? older[i] : null);
        }

        return merged;
    }
}
