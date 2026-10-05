using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;

namespace LedBalloon.Core.Tests;

/// <summary>
/// That the app is drawing with WLED's own compiled engine, and not quietly back on the ports.
/// <para>
/// Worth a test because the fallback is silent by design - a build with no engine beside it still
/// runs - so nothing else would notice if it stopped loading. Three of the
/// <c>*AgainstHardwareTests</c> files resolve their effects through <see cref="EffectLibrary"/> and
/// so measure the engine against numbers taken off the strip; if the engine fell back to the ports
/// those tests would keep passing and would stop meaning what they say.
/// </para>
/// </summary>
[Collection(EngineCollection.Name)]
public class NativeEngineTests
{
    [Fact]
    public void The_engine_is_what_the_library_hands_out()
    {
        Assert.True(
            EffectLibrary.UsingEngine,
            $"the native engine did not load: {EffectLibrary.EngineUnavailable}. "
                + "Build it with `pwsh native/build.ps1`.");

        // 143 of WLED 0.15.3's 187, the other 44 being the matrix-only ones that register as RSVD
        // with 2D compiled out - see native/README.md. Asserting a floor rather than the number
        // keeps this from breaking every time that changes.
        Assert.True(EffectLibrary.All.Count > 120, $"only {EffectLibrary.All.Count} effects");
        Assert.IsType<NativeEffect>(EffectLibrary.Find("Colorwaves"));
    }

    [Fact]
    public void An_effect_keeps_its_own_state_while_another_draws_between_its_frames()
    {
        // The engine has one segment and the app has a preview per effect, so each render hands its
        // own runtime in and takes it back. If that round-trip leaked, interleaved callers would
        // tread on each other - which they did, and which cost nine passing tests to find.
        IWledEffect first = EffectLibrary.Find("Colorwaves")!;
        IWledEffect other = EffectLibrary.Find("Rainbow")!;

        RgbColor[][] alone = Frames(first, 40, interleave: null);
        RgbColor[] interleaved = Frames(first, 40, interleave: other)[^1];

        // Not byte-equality. When two callers land on the same millisecond the engine draws the
        // second one three milliseconds late, so an interleaved caller can sit a fraction of a frame
        // off - see wled_render. What must hold is that it is still drawing its own effect and still
        // moving, so its last frame has to be one of the frames it draws when left alone.
        double closest = alone.Min(f => Difference(f, interleaved));
        Assert.True(closest < 2.0, $"interleaved output is {closest:F2} from anything it draws alone");
    }

    [Fact]
    public void Two_lengths_of_the_same_effect_do_not_disturb_each_other()
    {
        // Lengths differ between previews - a thumbnail is not the whole house - and resizing the
        // engine's segment must not reset it.
        IWledEffect effect = EffectLibrary.Find("Colorwaves")!;

        RgbColor[][] alone = Frames(effect, 40, interleave: null, length: 285);
        RgbColor[] shared = Frames(effect, 40, interleave: effect, length: 285, otherLength: 60)[^1];

        double closest = alone.Min(f => Difference(f, shared));
        Assert.True(closest < 2.0, $"a 60-pixel caller moved the 285-pixel one by {closest:F2}");
    }

    /// <summary>Mean channel difference out of 255, which is how this project measures agreement.</summary>
    private static double Difference(RgbColor[] a, RgbColor[] b)
    {
        if (a.Length != b.Length)
        {
            return double.MaxValue;
        }

        long total = 0;
        for (int i = 0; i < a.Length; i++)
        {
            total += Math.Abs(a[i].R - b[i].R) + Math.Abs(a[i].G - b[i].G) + Math.Abs(a[i].B - b[i].B);
        }

        return (double)total / (3 * a.Length);
    }

    private static RgbColor[][] Frames(
        IWledEffect effect,
        int frames,
        IWledEffect? interleave,
        int length = 285,
        int otherLength = 285)
    {
        var drawn = new List<RgbColor[]>(frames);
        var mine = new EffectSegment(length) { PaletteId = 11, FrameTime = 2 };
        var theirs = new EffectSegment(otherLength) { PaletteId = 11, FrameTime = 2 };

        for (int f = 0; f < frames; f++)
        {
            uint now = (uint)(f * 23);
            mine.Draw(effect, now);

            if (interleave is not null)
            {
                theirs.Draw(interleave, now);
            }

            drawn.Add([.. mine.Pixels]);
        }

        return [.. drawn];
    }
}
