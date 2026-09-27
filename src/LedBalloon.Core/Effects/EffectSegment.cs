using LedBalloon.Core.Models;

namespace LedBalloon.Core.Effects;

/// <summary>
/// One run's worth of LEDs plus the settings an effect reads, which together are everything an
/// effect gets to see.
/// <para>
/// The pixels are the reason this exists. Drawing a run used to be a function of position - ask it
/// what color the LED two thirds along is and it answers - which works for anything that is a
/// gradient sliding along a strip and for nothing else. Plenty of effects read the frame they drew
/// last: trails, fades, sparks that decay. Those cannot be answered from a position, only
/// accumulated, so a run needs somewhere to accumulate.
/// </para>
/// </summary>
public sealed class EffectSegment
{
    public EffectSegment(int length)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);

        Length = length;
        Pixels = new RgbColor[length];
    }

    /// <summary>How many LEDs. WLED calls this SEGLEN.</summary>
    public int Length { get; }

    /// <summary>What the run is showing right now, and what the next frame is drawn over.</summary>
    public RgbColor[] Pixels { get; }

    /// <summary>The three color slots, primary first. WLED's SEGCOLOR.</summary>
    public RgbColor[] Colors { get; set; } = [RgbColor.White, RgbColor.Black, RgbColor.Black];

    /// <summary>
    /// Which segment this is on the controller, which two effects fold into their seed.
    /// <para>
    /// Fairy and Fairytwinkle both start their pattern at <c>5100 + id</c>, so the same effect on the
    /// same run looks different depending on which segment it is - deliberately, so that a strip cut
    /// into segments does not show the same twinkle twice. Nothing else reads it, and it is only here
    /// because those two cannot be checked against the strip without it.
    /// </para>
    /// </summary>
    public int SegmentId { get; set; }

    /// <summary>
    /// How many segments the controller has, which decides how much scratch memory this one may use.
    /// <para>
    /// Only the two fireworks effects read it, and only to cap how many particles they can afford - a
    /// segment's share of the effect memory doubles if the controller is using half its segments or
    /// fewer and doubles again at a quarter. Anything up to eight segments on an ESP32 gives the same
    /// answer, so the default of one is right for every controller here; it is modelled because a
    /// controller carved into more than eight segments would genuinely show fewer sparks.
    /// </para>
    /// </summary>
    public int ActiveSegments { get; set; } = 1;

    /// <summary>
    /// How long one frame lasts on the controller driving this run.
    /// <para>
    /// Effects read it: Blink measures its duty cycle in frames, and anything that fades reaches a
    /// given darkness after a number of them rather than after an amount of time.
    /// </para>
    /// </summary>
    public int FrameMilliseconds { get; set; } = 1000 / 42;

    /// <summary>
    /// WLED's <c>FRAMETIME</c>, which is a different number from <see cref="FrameMilliseconds"/>
    /// and used for a different purpose.
    /// <para>
    /// <see cref="FrameMilliseconds"/> is how often a frame actually gets drawn. This is a constant
    /// the firmware derives from the configured frame rate, which a handful of effects then use in
    /// their own arithmetic - and with the rate set to unlimited, as both controllers here are, it
    /// comes to two milliseconds rather than the nine a frame really takes.
    /// </para>
    /// <para>
    /// Mixing the two up is not harmless. Blink's cycle is
    /// <c>(255 - speed) * 20 + FRAMETIME * 2</c>, so at the top of the speed slider the whole cycle
    /// <em>is</em> FRAMETIME: 4 ms with the right constant and 18 ms with the frame interval, which
    /// is four and a half times too slow.
    /// </para>
    /// </summary>
    public int FrameTime { get; set; } = Effects.FrameTime.MinimumFrameDelay;

    /// <summary>
    /// How long this effect wants before it is drawn again, which most of them leave alone.
    /// <para>
    /// WLED's effects return this, and the strip's service loop schedules the next frame from it.
    /// Almost all of them return <c>FRAMETIME</c>, meaning as soon as the strip can manage - but
    /// some do not, and for those it is not a detail. Chase Flash spends 20 ms on a flash frame and
    /// 30 on the gap between, and ICU stops dead for a second or three between eye movements.
    /// Neither is reproducible by drawing every frame and neither reads right without it.
    /// </para>
    /// <para>
    /// Set back to <see cref="FrameTime"/> before each frame by <see cref="Draw"/>, so an effect
    /// that asks for a delay once does not keep it forever.
    /// </para>
    /// </summary>
    public int FrameDelay { get; set; } = Effects.FrameTime.MinimumFrameDelay;

    /// <summary>
    /// Scratch that survives between frames, WLED's <c>SEGENV.step</c>. Effects that need to know
    /// what they did last time keep it here.
    /// </summary>
    public uint Step { get; set; }

    /// <summary>Frames drawn since this run started, WLED's <c>SEGENV.call</c>.</summary>
    public uint Call { get; set; }

    /// <summary>
    /// Two more scratch values that survive between frames, WLED's <c>aux0</c> and <c>aux1</c> - and
    /// sixteen bits each, unlike <see cref="Step"/>, which is thirty-two.
    /// <para>
    /// The width is part of the behaviour, not an implementation detail. An effect that accumulates into
    /// one of these is relying on it to wrap at 65536: Pacifica keeps two of its four color-index
    /// counters here and the other two packed into <see cref="Step"/>, and adds tens of thousands to each
    /// every frame. Held in thirty-two bits they would climb away instead of cycling, and the wave would
    /// stop being periodic. Read as <see cref="uint"/> so that no effect has to cast, but stored narrow.
    /// </para>
    /// </summary>
    public uint Aux0
    {
        get => _aux0;
        set => _aux0 = (ushort)value;
    }

    /// <inheritdoc cref="Aux0"/>
    public uint Aux1
    {
        get => _aux1;
        set => _aux1 = (ushort)value;
    }

    private ushort _aux0;
    private ushort _aux1;

    /// <summary>
    /// The effect option checkboxes, WLED's <c>check1</c>, <c>check2</c> and <c>check3</c>.
    /// <para>
    /// Unlabelled here because each effect names them itself: the second is usually Overlay, which
    /// means "draw on what is already there rather than clearing first", but Rolling Balls uses all
    /// three for collisions, overlay and trails.
    /// </para>
    /// </summary>
    public bool Option1 { get; set; }

    public bool Option2 { get; set; }

    public bool Option3 { get; set; }

    /// <summary>
    /// Randomness, seeded rather than free-running.
    /// <para>
    /// Some effects sprinkle pixels about at random, so a preview of one can never match the strip
    /// frame for frame - only in how much it lights and how often. Seeding it at least makes the
    /// preview repeatable, so a test that measures those can be believed.
    /// </para>
    /// </summary>
    public Random Random { get; set; } = new(11337);

    /// <summary>
    /// The controller's own generator, with its seed exposed - for the effects that use randomness as
    /// a pattern rather than as noise. See <see cref="SeededSequence"/> for why there are two.
    /// </summary>
    public SeededSequence Sequence { get; } = new();

    private object? _scratch;

    /// <summary>
    /// Whatever this effect needs to remember between frames, created the first time it asks.
    /// <para>
    /// Stands in for WLED's <c>SEGENV.data</c>, which is a raw byte array an effect casts to
    /// whatever it likes. Typed here instead, because the reason the byte array exists - a
    /// microcontroller with no heap to spare - does not apply.
    /// </para>
    /// </summary>
    public T Scratch<T>(Func<T> create) where T : class
    {
        ArgumentNullException.ThrowIfNull(create);

        if (_scratch is not T existing)
        {
            existing = create();
            _scratch = existing;
        }

        return existing;
    }

    /// <summary>
    /// Draws one frame of <paramref name="effect"/>, counting it.
    /// <para>
    /// The count is the point. A handful of effects read <see cref="Call"/> - to know they are on
    /// their first frame and have nothing to carry forward, or, in Chase Rainbow's case, to walk the
    /// color wheel one step per frame - and in WLED it is the strip's service loop that keeps it,
    /// not the effect. So anything standing in for that loop has to come through here rather than
    /// calling <see cref="IWledEffect.Render"/> itself, or those effects sit on frame zero forever.
    /// </para>
    /// </summary>
    public void Draw(IWledEffect effect, uint now)
    {
        ArgumentNullException.ThrowIfNull(effect);

        // Asking for nothing in particular is the usual answer, so it is the one an effect gets
        // without saying anything.
        FrameDelay = FrameTime;

        effect.Render(this, now);

        unchecked
        {
            Call++;
        }
    }

    /// <summary>Whether the run is wired back to front, which some effects mirror themselves for.</summary>
    public bool Reverse { get; set; }

    public byte Speed { get; set; } = 128;
    public byte Intensity { get; set; } = 128;
    public byte Custom1 { get; set; } = 128;
    public byte Custom2 { get; set; } = 128;
    public byte Custom3 { get; set; } = 16;

    /// <summary>Which palette, by WLED's numbering. Zero means "use the color slots instead".</summary>
    public int PaletteId { get; set; }

    /// <summary>
    /// The controller's colour blending setting, <c>hw.led.cb</c>: 0 wraps only while something is
    /// moving, 1 always wraps, 2 never wraps, 3 turns blending off altogether.
    /// <para>
    /// One setting, and three separate things depend on it - whether a palette lookup wraps round to
    /// its first entry, whether the lookup is cut short of the palette's end, and whether it lands
    /// between two entries or on one. Kept as the single number the firmware keeps, with the rest
    /// derived, because holding them apart is how they drift out of agreement.
    /// </para>
    /// <para>
    /// Zero on both controllers here, which is the stock setting.
    /// </para>
    /// </summary>
    public int PaletteBlend { get; set; }

    /// <summary>
    /// Whether a palette lookup wraps back to its first entry at the top, WLED's
    /// <c>PALETTE_SOLID_WRAP</c>. It changes what the last few LEDs of a run do.
    /// </summary>
    public bool SolidWrap => PaletteBlend is 1 or 3;

    /// <summary>
    /// The gradient behind <see cref="PaletteId"/>, as the controller reports it.
    /// <para>
    /// Read from <c>/json/palx</c>, which already serves the sixteen-entry gamma-corrected form the
    /// effect samples rather than the raw palette file - so this needs no correcting on the way in.
    /// </para>
    /// </summary>
    public WledPalette? Palette { get; set; }

    /// <summary>
    /// The color an effect gets when it asks the palette for index <paramref name="index"/>.
    /// </summary>
    /// <param name="mapping">
    /// True when the index is an LED position rather than a palette position, so it is stretched
    /// across the palette first. That is how an effect paints the whole gradient along the run.
    /// </param>
    /// <param name="wrap">
    /// False - the usual - stops the lookup short of the palette's end, so the last color does not
    /// blend back round into the first.
    /// </param>
    /// <param name="colorSlot">
    /// Which color slot stands in when the run is not on a palette.
    /// <para>
    /// Only 0, 1 and 2 are handled. WLED allows an out-of-range value as a way of saying "use the
    /// gradient even on palette zero", since its own check is <c>palette == 0 &amp;&amp; mcol &lt; 3</c>
    /// - Glitter passes 255 for exactly that. Nothing ported needs it yet, and Glitter is waiting on
    /// it along with the raw color-blending setting it also reads.
    /// </para>
    /// </param>
    /// <param name="brightness">Scales the result, which some effects use to pulse.</param>
    /// <param name="blend">
    /// False lands on one of the palette's own stops instead of interpolating between them.
    /// </param>
    public RgbColor ColorFromPalette(
        int index,
        bool mapping = false,
        bool wrap = false,
        int colorSlot = 0,
        byte brightness = 255,
        bool? blend = null)
    {
        // On palette Default the color slot is the answer - but only for a real slot. WLED's own
        // check is `palette == 0 && mcol < 3`, which lets an effect pass something out of range to
        // mean "use the gradient anyway". Glitter does exactly that.
        if (Palette is null || (PaletteId == 0 && (uint)colorSlot < 3))
        {
            RgbColor flat = Colors[Math.Clamp(colorSlot, 0, Colors.Length - 1)];
            return brightness == 255 ? flat : Fade(flat, brightness);
        }

        int at = index;

        if (mapping && Length > 1)
        {
            at = index * 255 / (Length - 1);
        }

        byte wrapped = (byte)(at & 0xFF);

        if (!wrap && PaletteBlend != 3)
        {
            wrapped = FastLed.Scale8(wrapped, 240);
        }

        // Without blending the lookup lands on one of the palette's sixteen stops rather than
        // between two of them, which is what gives a twinkle its distinct colors instead of a wash.
        // Off altogether is what blending setting 3 means; an effect can also ask for it directly.
        bool blending = blend ?? PaletteBlend != 3;

        // Unblended has to land exactly on a stop, and a sixteen entry palette keeps its stops at
        // multiples of sixteen - so the top four bits of the index pick one by multiplying by
        // sixteen, not by dividing the sixteen entries across the whole range. Spreading them
        // evenly instead lands a unit past every stop but the first and last, which is a color
        // nobody chose: Fire 2012 reads its heat back off the palette's own steps, and only 85% of
        // its pixels sat on one until this was right.
        double position = blending ? wrapped / 255d : (wrapped >> 4) * 16 / 255d;

        RgbColor color = Palette.ColorAt(position, Colors[0], Colors[1], Colors[2]);

        return brightness == 255 ? color : Fade(color, brightness);
    }

    /// <summary>
    /// A position on a red-green-blue-red wheel, or the same position on the palette when one is
    /// chosen.
    /// <para>
    /// The palette branch is not a nicety: on any palette but Default this stops being a rainbow
    /// and becomes a walk along whatever gradient is set, which is why Rainbow on a custom palette
    /// shows that palette rather than a rainbow.
    /// </para>
    /// </summary>
    public RgbColor ColorWheel(byte position)
    {
        if (PaletteId != 0 && Palette is not null)
        {
            return ColorFromPalette(position, mapping: false, wrap: true);
        }

        var pos = (byte)(255 - position);

        if (pos < 85)
        {
            return new RgbColor((byte)(255 - (pos * 3)), 0, (byte)(pos * 3));
        }

        if (pos < 170)
        {
            pos -= 85;
            return new RgbColor(0, (byte)(pos * 3), (byte)(255 - (pos * 3)));
        }

        pos -= 170;
        return new RgbColor((byte)(pos * 3), (byte)(255 - (pos * 3)), 0);
    }

    /// <summary>
    /// A random byte below <paramref name="limit"/>, or any byte when the limit is zero.
    /// <para>
    /// Drawn from this run's own generator rather than a shared one, so a preview is repeatable:
    /// the same scene drawn twice looks the same. It cannot match the controller's own sequence
    /// and is not meant to, so effects built on it are checked by how they behave in aggregate
    /// rather than pixel for pixel.
    /// </para>
    /// </summary>
    public byte Random8() => (byte)Random.Next(256);

    /// <summary>
    /// A random byte below <paramref name="limit"/>, scaled the way FastLED scales it.
    /// <para>
    /// <c>(r * limit) &gt;&gt; 8</c> rather than a modulus, which matters at the edges: a limit of
    /// zero always gives zero. Effects rely on that. Sparkle Dark's odds are
    /// <c>random8((255 - intensity) &gt;&gt; 4)</c>, which is zero once intensity passes 239 - so at
    /// the top of that slider the throw always succeeds and it flashes every frame. Treating a limit
    /// of zero as "any byte" instead would make the top of the slider the quietest setting.
    /// </para>
    /// </summary>
    public byte Random8(int limit) =>
        limit <= 0 ? (byte)0 : (byte)(Random.Next(256) * limit >> 8);

    /// <summary>
    /// A random byte from <paramref name="lowest"/> up to but not including
    /// <paramref name="limit"/>, FastLED's two-argument <c>random8</c>.
    /// </summary>
    public byte Random8(int lowest, int limit) => (byte)(lowest + Random8(limit - lowest));

    /// <summary>A random 16-bit value.</summary>
    public ushort Random16() => (ushort)Random.Next(65536);

    /// <summary>A random 16-bit value below <paramref name="limit"/>, scaled as FastLED scales it.</summary>
    public ushort Random16(int limit) =>
        limit <= 0 ? (ushort)0 : (ushort)((long)Random.Next(65536) * limit >> 16);

    /// <summary>
    /// A sixteen-bit draw between <paramref name="lowest"/> and <paramref name="limit"/>, which
    /// FastLED builds from the one-argument form rather than scaling the whole range.
    /// </summary>
    public ushort Random16(int lowest, int limit) => (ushort)(lowest + Random16(limit - lowest));

    /// <summary>
    /// A random place on the color wheel at least 42 of 255 away from <paramref name="from"/>.
    /// <para>
    /// The distance is what makes it useful: effects that swap to "another random color" would
    /// otherwise sometimes swap to one nobody could tell from the old one, and read as a stall.
    /// Forty-two of 255 is a sixth of the wheel, so the new color is always visibly a new color.
    /// </para>
    /// </summary>
    public byte RandomWheelIndex(byte from)
    {
        while (true)
        {
            byte candidate = Random8();
            int apart = Math.Abs(from - candidate);

            // Round the wheel either way, so red next to red is close however it is reached.
            if (Math.Min(apart, 255 - apart) >= 42)
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// How bright a color reads overall, which is how WLED decides what shows through what.
    /// <para>
    /// FastLED's <c>getAverageLight</c>, which is not the mean of the three channels however much it
    /// looks like one: each channel is scaled by 85 and the three results added, so the rounding happens
    /// three times instead of once. A flat grey 100 comes out 99. Colortwinkles compares a twinkle
    /// against its background with this and Pacifica decides where to add white crests by it, and both
    /// are comparisons against a threshold - which is exactly where a unit either way changes the answer.
    /// </para>
    /// </summary>
    public static byte AverageLight(RgbColor color) => (byte)(
        FastLed.Scale8(color.R, 85) + FastLed.Scale8(color.G, 85) + FastLed.Scale8(color.B, 85));

    /// <summary>Scales a color down with no floor, unlike <see cref="Fade"/>.</summary>
    public static RgbColor Scale(RgbColor color, byte amount) => new(
        FastLed.Scale8(color.R, amount),
        FastLed.Scale8(color.G, amount),
        FastLed.Scale8(color.B, amount));

    /// <summary>Scales a color down, never quite to nothing while it is still lit.</summary>
    public static RgbColor Fade(RgbColor color, byte amount)
    {
        if (amount == 0)
        {
            return RgbColor.Black;
        }

        // The "video" guard adds one to every lit channel, not only to one that would otherwise round
        // away. It reads like a floor and is not one: fading 255 by 52 gives 52 rather than 51,
        // because the one is added whatever the product came to. Lightning is where that shows -
        // it dims the primary by 52, 127 or 255, and the strip answers 52, 127 and 255 where a plain
        // floor would answer 51, 126 and 255.
        return new RgbColor(
            FastLed.Scale8Video(color.R, amount),
            FastLed.Scale8Video(color.G, amount),
            FastLed.Scale8Video(color.B, amount));
    }

    /// <summary>Mixes two colors, where 0 is all of the first and 255 all of the second.</summary>
    public static RgbColor Blend(RgbColor first, RgbColor second, byte amount) =>
        amount switch
        {
            0 => first,
            255 => second,
            _ => new RgbColor(
                (byte)(((second.R * amount) + (first.R * (255 - amount))) >> 8),
                (byte)(((second.G * amount) + (first.G * (255 - amount))) >> 8),
                (byte)(((second.B * amount) + (first.B * (255 - amount))) >> 8)),
        };

    /// <summary>Lights one LED, ignoring one off the end rather than throwing.</summary>
    public void SetPixel(int index, RgbColor color)
    {
        if ((uint)index < (uint)Length)
        {
            Pixels[index] = color;
        }
    }

    /// <summary>
    /// Pulls every LED that fraction of the way to black, which is not the same as
    /// <see cref="FadeOut"/>.
    /// <para>
    /// <see cref="FadeOut"/> fades toward the secondary color, so a trail on a colored background
    /// settles into that background. This goes to black whatever the background is, which is what
    /// the effects that draw dots on nothing want.
    /// </para>
    /// </summary>
    /// <param name="amount">How much to take off, where 255 is all of it.</param>
    public void FadeToBlack(byte amount)
    {
        if (amount == 0)
        {
            return;
        }

        var keep = (byte)(255 - amount);

        for (int i = 0; i < Pixels.Length; i++)
        {
            Pixels[i] = Scale(Pixels[i], keep);
        }
    }

    /// <summary>Adds two colors, each channel saturating at 255 rather than wrapping.</summary>
    public static RgbColor Add(RgbColor first, RgbColor second) =>
        first == RgbColor.Black ? second
        : second == RgbColor.Black ? first
        : new RgbColor(
            FastLed.QAdd8(first.R, second.R),
            FastLed.QAdd8(first.G, second.G),
            FastLed.QAdd8(first.B, second.B));

    /// <summary>
    /// Softens the run by bleeding each LED into its two neighbors.
    /// <para>
    /// A single pass in one direction carrying a remainder along, rather than a symmetrical average -
    /// so the smear is not quite even, and repeating it every frame is what turns a point into a
    /// glow. Each LED keeps all but <paramref name="amount"/> of itself and gives half of that away
    /// in each direction.
    /// </para>
    /// </summary>
    /// <param name="smear">
    /// True to let the LED keep all of itself as well as receive, which brightens rather than
    /// spreads.
    /// </param>
    public void Blur(byte amount, bool smear = false)
    {
        if (amount == 0)
        {
            return;
        }

        var keep = (byte)(smear ? 255 : 255 - amount);
        var seep = (byte)(amount >> (smear ? 2 : 1));

        RgbColor carried = RgbColor.Black;
        RgbColor lastNew = RgbColor.Black;

        for (int i = 0; i < Pixels.Length; i++)
        {
            RgbColor current = Pixels[i];

            // Scaled rather than faded: no video guard here, so what bleeds sideways really does
            // reach zero. With the guard a dim pixel can never quite go out, and a blur applied every
            // frame then quietly adds light instead of only moving it - which showed up as Fireworks
            // holding two and a half times as much red as the strip.
            RgbColor part = Scale(current, seep);
            RgbColor kept = Scale(current, keep);

            if (i > 0)
            {
                if (carried != RgbColor.Black)
                {
                    kept = Add(kept, carried);
                }

                Pixels[i - 1] = Add(lastNew, part);
            }
            else
            {
                Pixels[i] = kept;
            }

            lastNew = kept;
            carried = part;
        }

        Pixels[^1] = lastNew;
    }

    /// <summary>Paints the whole run one color.</summary>
    public void Fill(RgbColor color) => Array.Fill(Pixels, color);

    /// <summary>
    /// Pulls every LED a fraction of the way toward the secondary color, which is what leaves a
    /// trail behind anything that moves.
    /// <para>
    /// Once per frame, so how long a trail looks depends on how fast the controller is drawing.
    /// That is not a detail worth smoothing away: it is why the same effect trails differently on
    /// a short run and a long one.
    /// </para>
    /// </summary>
    /// <param name="rate">WLED's rate, where larger fades slower. 254 halves the distance each frame.</param>
    public void FadeOut(byte rate)
    {
        int adjusted = (256 - rate) >> 1;
        int mappedRate = 256 / (adjusted + 1);

        RgbColor background = Colors[1];

        for (int i = 0; i < Pixels.Length; i++)
        {
            RgbColor pixel = Pixels[i];

            Pixels[i] = new RgbColor(
                FastLed.FadeChannel(pixel.R, background.R, mappedRate),
                FastLed.FadeChannel(pixel.G, background.G, mappedRate),
                FastLed.FadeChannel(pixel.B, background.B, mappedRate));
        }
    }

    /// <summary>Copies the settings a preset or live state describes onto this run.</summary>
    public void Adopt(WledSegment wled, WledPalette? palette)
    {
        ArgumentNullException.ThrowIfNull(wled);

        Speed = wled.Speed ?? 128;
        Intensity = wled.Intensity ?? 128;
        Custom1 = wled.Custom1 ?? 128;
        Custom2 = wled.Custom2 ?? 128;
        Custom3 = wled.Custom3 ?? 16;
        PaletteId = wled.Palette ?? 0;
        SegmentId = wled.Id ?? 0;
        Option3 = wled.Option3 ?? false;
        Palette = palette;
        Reverse = wled.Reverse ?? false;
        Option1 = wled.Option1 ?? false;
        Option2 = wled.Option2 ?? false;

        Colors =
        [
            wled.Colors is { Length: > 0 } ? wled.PrimaryColor : RgbColor.White,
            wled.Colors is { Length: > 1 } ? wled.SecondaryColor : RgbColor.Black,
            wled.Colors is { Length: > 2 } ? RgbColor.FromWledArray(wled.Colors[2]) : RgbColor.Black,
        ];
    }
}
