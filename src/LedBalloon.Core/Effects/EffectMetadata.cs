namespace LedBalloon.Core.Effects;

/// <summary>
/// What one effect says it uses, read from the controller's own <c>/json/fxdata</c>.
/// <para>
/// WLED effects differ enormously in what they read. Colortwinkles ignores the color slots
/// entirely and draws from the palette; Solid Glitter is the other way round and ignores the
/// palette; Bpm uses one color and the palette. Showing three swatches and a palette picker
/// regardless is wrong in both directions — it offers controls that do nothing, and hides which
/// of them the effect actually cares about.
/// </para>
/// <para>
/// The metadata is per firmware build, so it is read from the device rather than shipped. Two
/// controllers on the same house can run different builds with different effect lists.
/// </para>
/// </summary>
public sealed class EffectMetadata
{
    /// <summary>The slot labels WLED falls back to when an effect asks for one without naming it.</summary>
    private static readonly string[] DefaultSlotNames = ["Color", "Background", "Custom"];

    /// <summary>An effect that told us nothing, so every control is offered.</summary>
    public static EffectMetadata Unknown { get; } = new()
    {
        Declared = false,
        ColorSlots = DefaultSlotNames,
        PaletteLabel = "Palette",

        // An effect that says nothing gets every control, the same as its color slots do.
        SpeedLabel = "Speed",
        IntensityLabel = "Intensity",
    };

    /// <summary>
    /// False when the effect ships no metadata at all. WLED's own UI then shows everything, on the
    /// grounds that offering a control that does nothing beats hiding one that works.
    /// </summary>
    public bool Declared { get; private init; }

    /// <summary>
    /// Three entries, one per color slot. Null means the effect does not read that slot; otherwise
    /// it is what to call it — "Glitter color", "Trail", "Peaks" — because several effects name
    /// their slots and the name is the only documentation there is.
    /// </summary>
    public IReadOnlyList<string?> ColorSlots { get; private init; } = [null, null, null];

    /// <summary>What to call the palette picker, or null when the effect ignores the palette.</summary>
    public string? PaletteLabel { get; private init; }

    /// <summary>The effect's own names for its sliders, which are rarely "speed" and "intensity".</summary>
    public IReadOnlyList<string> Sliders { get; private init; } = [];

    /// <summary>
    /// What the effect's three extra sliders are called, or null for one it does not read.
    /// </summary>
    /// <remarks>
    /// Positions three to five of the slider section. Fire 2012 calls its two "2D Blur" and "Boost";
    /// Palette calls its one "Rotation". An effect with one of these and no way to move it is an
    /// effect only half reachable.
    /// </remarks>
    public IReadOnlyList<string?> CustomLabels { get; private init; } = [null, null, null];

    /// <summary>What the effect's three tick boxes are called, or null for one it does not read.</summary>
    /// <remarks>
    /// Positions six to eight. Palette's first is "Animate Shift", and whether it is ticked is the
    /// difference between a gradient lying still and one scrolling.
    /// </remarks>
    public IReadOnlyList<string?> OptionLabels { get; private init; } = [null, null, null];

    /// <summary>
    /// What the effect wants its controls set to when it is chosen.
    /// </summary>
    /// <remarks>
    /// The last section, as <c>ix=112,c1=0,o1=1,o2=0,o3=1</c>. WLED's own UI applies these the
    /// moment an effect is picked, which is why the same effect can look like two different things
    /// depending on which app selected it: an app that sends only the effect number leaves the
    /// segment carrying whatever the last effect was set to.
    /// <para>
    /// Measured on 192.0.2.12: Palette with its own defaults draws 244 colors across 285 LEDs and
    /// scrolls; with the flags left as they were it draws the same 244 and sits still. Keys other
    /// than the eight controls - <c>m12</c> for how a 2D effect maps itself, <c>si</c> for which
    /// sound input it reads - are the firmware's business and are kept out.
    /// </para>
    /// </remarks>
    public IReadOnlyDictionary<string, int> Defaults { get; private init; } =
        new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>The eight controls a segment carries, which are the only defaults worth sending.</summary>
    private static readonly string[] Controls = ["sx", "ix", "c1", "c2", "c3", "o1", "o2", "o3"];

    /// <summary>
    /// What the effect calls its speed slider, or null when it has no use for one.
    /// </summary>
    /// <remarks>
    /// The slider section is positional - speed, intensity, then three custom sliders and three
    /// checkboxes - and an empty entry means the effect ignores that control. Reading it as a list
    /// with the blanks dropped, which is what <see cref="Sliders"/> does, loses which is which:
    /// Fire 2012 declares "Cooling,Spark rate,,2D Blur,Boost", and the blank in the middle is the
    /// difference between its last two sliders being custom 2 and 3 or custom 1 and 2.
    /// </remarks>
    public string? SpeedLabel { get; private init; }

    /// <inheritdoc cref="SpeedLabel"/>
    public string? IntensityLabel { get; private init; }

    public bool UsesSpeed => SpeedLabel is not null;

    public bool UsesIntensity => IntensityLabel is not null;

    /// <summary>
    /// The dimension and audio flags, verbatim. "1" and "2" are the dimensions the effect supports,
    /// "v" and "f" that it reacts to volume or to frequency.
    /// </summary>
    public string Flags { get; private init; } = string.Empty;

    /// <summary>
    /// True for an effect that only makes sense on a 2D matrix, so a run of LED along a roofline
    /// cannot show it at all.
    /// <para>
    /// WLED's own UI hides these on a 1D segment, and a list that offers them is a list of 37 names
    /// that do nothing. Checked against the controller: this rule and the UI agree on all 37.
    /// </para>
    /// </summary>
    public bool Is2DOnly => Flags.Contains('2') && !Flags.Contains('1');

    /// <summary>True for an effect driven by sound, which needs a microphone or a UDP audio feed.</summary>
    public bool IsAudioReactive => Flags.Contains('v') || Flags.Contains('f');

    public bool UsesPalette => PaletteLabel is not null;

    /// <summary>Which slots to show, as 1-based numbers in the order the user would read them.</summary>
    public IReadOnlyList<int> UsedSlots =>
        [.. Enumerable.Range(0, ColorSlots.Count).Where(i => ColorSlots[i] is not null).Select(i => i + 1)];

    /// <summary>True when the palette is the only thing deciding what color this effect draws.</summary>
    public bool PaletteOnly => UsesPalette && UsedSlots.Count == 0;

    /// <summary>
    /// Parses one entry of <c>/json/fxdata</c>.
    /// <para>
    /// The format is five optional <c>;</c>-separated sections: sliders, colors, palette, flags,
    /// defaults. Within the first three, a <c>!</c> means "used, call it whatever you normally
    /// would" and an empty entry means "not used".
    /// </para>
    /// <para>
    /// The distinction that matters is between a section that is <em>missing</em> and one that is
    /// <em>present and empty</em>. An effect with no metadata at all — of which there are two,
    /// Solid and Oscillate — shows everything, while <c>"!;;"</c> declares both colors and palette
    /// unused. Treating those the same made Oscillate look like it ignored the palette when the
    /// controller's own UI offers one.
    /// </para>
    /// </summary>
    public static EffectMetadata Parse(string? fxdata)
    {
        if (string.IsNullOrEmpty(fxdata))
        {
            return Unknown;
        }

        string[] sections = fxdata.Split(';');

        string[] colors = Section(sections, 1);
        string[] palette = Section(sections, 2);

        var slots = new string?[3];
        for (int i = 0; i < 3; i++)
        {
            string entry = i < colors.Length ? colors[i].Trim() : string.Empty;

            slots[i] = entry.Length == 0 ? null
                : entry == "!" ? DefaultSlotNames[i]
                : entry;
        }

        string head = palette.Length > 0 ? palette[0].Trim() : string.Empty;

        return new EffectMetadata
        {
            Declared = true,
            ColorSlots = slots,

            // A number here is not a label. WLED's own UI excludes it with isNaN(), and treating it
            // as one would put a stray digit where the picker's name goes.
            PaletteLabel = head.Length == 0 || IsNumber(head) ? null
                : head == "!" ? "Palette"
                : head,

            Sliders = [.. Section(sections, 0).Select(s => s.Trim()).Where(s => s.Length > 0)],
            SpeedLabel = SliderLabel(sections, 0, "Speed"),
            IntensityLabel = SliderLabel(sections, 1, "Intensity"),
            CustomLabels = [.. Enumerable.Range(2, 3).Select(i => SliderLabel(sections, i, null))],
            OptionLabels = [.. Enumerable.Range(5, 3).Select(i => SliderLabel(sections, i, null))],
            Flags = sections.Length > 3 ? sections[3].Trim() : string.Empty,
            Defaults = ParseDefaults(sections.Length > 4 ? sections[4] : string.Empty),
        };
    }

    /// <summary>
    /// One positional slider label: null when the effect ignores it, the default when it asks for
    /// one without naming it, and otherwise the name it gave.
    /// </summary>
    private static string? SliderLabel(string[] sections, int at, string? fallback)
    {
        string[] slots = Section(sections, 0);
        string entry = at < slots.Length ? slots[at].Trim() : string.Empty;

        return entry.Length == 0 ? null : entry == "!" ? fallback : entry;
    }

    /// <summary>
    /// The <c>name=number</c> list the effect ends with, keeping only the eight real controls.
    /// </summary>
    private static IReadOnlyDictionary<string, int> ParseDefaults(string section)
    {
        var wanted = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (string pair in section.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] halves = pair.Split('=', 2);

            if (halves.Length == 2 &&
                Controls.Contains(halves[0].Trim(), StringComparer.Ordinal) &&
                int.TryParse(halves[1].Trim(),
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out int value))
            {
                wanted[halves[0].Trim()] = value;
            }
        }

        return wanted;
    }

    /// <summary>Reads every effect's metadata, lined up with the effect list by index.</summary>
    public static IReadOnlyList<EffectMetadata> ParseAll(IEnumerable<string>? fxdata) =>
        fxdata is null ? [] : [.. fxdata.Select(Parse)];

    /// <summary>
    /// What effect <paramref name="effectId"/> uses, or <see cref="Unknown"/> when this controller
    /// has not said.
    /// <para>
    /// The fall-back matters more than it looks. <c>/json/fxdata</c> arrived in WLED 0.14, so older
    /// firmware answers 404 and there is nothing to line up against; a build could also report
    /// fewer entries than effects. Both land here, and both come back as "offer everything" — which
    /// is wrong in the safe direction, since a control that does nothing is a smaller failure than
    /// a hidden control that would have worked.
    /// </para>
    /// </summary>
    public static EffectMetadata For(IReadOnlyList<EffectMetadata>? all, int? effectId) =>
        all is not null && effectId is { } id && id >= 0 && id < all.Count ? all[id] : Unknown;

    /// <summary>A missing section and an empty one both mean "nothing declared here".</summary>
    private static string[] Section(string[] sections, int index) =>
        index >= sections.Length || sections[index].Length == 0 ? [] : sections[index].Split(',');

    private static bool IsNumber(string text) =>
        double.TryParse(text, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out _);

    public override string ToString()
    {
        string slots = UsedSlots.Count == 0
            ? "no colors"
            : string.Join(" + ", UsedSlots.Select(n => ColorSlots[n - 1]));

        return $"{slots}; {(UsesPalette ? PaletteLabel : "no palette")}" +
               (Declared ? string.Empty : " (not declared)");
    }
}
