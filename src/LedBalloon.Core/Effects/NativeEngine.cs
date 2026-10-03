using System.Runtime.InteropServices;
using LedBalloon.Core.Models;

namespace LedBalloon.Core.Effects;

/// <summary>
/// WLED's own effect engine, compiled and called rather than transliterated. See native/README.md.
/// <para>
/// The engine holds one segment, and this app has many previews alive at once - a thumbnail per
/// effect. So nothing is left in the engine between frames: each render hands in that preview's
/// runtime, draws, and takes the runtime back. <see cref="EffectSegment"/> already carries
/// <see cref="EffectSegment.Step"/>, <see cref="EffectSegment.Call"/> and the two aux words for
/// exactly this reason; the only piece it did not already have is WLED's per-segment data buffer,
/// which lives in <see cref="EffectSegment.Scratch{T}"/> as bytes.
/// </para>
/// <para>
/// Windows only, for now. The engine is a native DLL built by native/build.ps1, and there is
/// nothing behind it: <see cref="IsReady"/> is false anywhere it cannot be loaded, and then nothing
/// can be drawn. <see cref="EffectLibrary.EngineUnavailable"/> is how that gets said out loud.
/// </para>
/// </summary>
public static class NativeEngine
{
    private const string Dll = "wledfx";

    /// <summary>Serialises access, because the engine has one segment and callers have many.</summary>
    private static readonly object Gate = new();

    private static readonly Lazy<bool> Loaded = new(TryLoad, LazyThreadSafetyMode.ExecutionAndPublication);

    private static uint[] _scratchPixels = [];
    private static int _pixelCount;

    /// <summary>True when the engine loaded and can be asked for frames.</summary>
    public static bool IsReady => Loaded.Value;

    /// <summary>Why the engine is not available, when it is not.</summary>
    public static string? Unavailable { get; private set; }

    /// <summary>How many effects the engine registered. 187 on WLED 0.15.3.</summary>
    public static int EffectCount => IsReady ? Native.ModeCount() : 0;

    /// <summary>
    /// The effect's name and its fxdata, as the firmware's own table holds it: "Name@sliders;colours;
    /// palette;flags;defaults".
    /// </summary>
    public static string EffectData(int mode) =>
        IsReady ? Marshal.PtrToStringUTF8(Native.ModeDataPtr((byte)mode)) ?? "" : "";

    /// <summary>The effect's name alone.</summary>
    public static string EffectName(int mode)
    {
        string data = EffectData(mode);
        int at = data.IndexOf('@', StringComparison.Ordinal);
        return at < 0 ? data : data[..at];
    }

    /// <summary>Every effect the engine offers, in its own order, ready to draw.</summary>
    public static IReadOnlyList<IWledEffect> All()
    {
        if (!IsReady)
        {
            return [];
        }

        var out_ = new List<IWledEffect>(EffectCount);
        for (int mode = 0; mode < EffectCount; mode++)
        {
            string name = EffectName(mode);
            if (name.Length > 0 && name != "RSVD")
            {
                out_.Add(new NativeEffect(mode, name));
            }
        }

        return out_;
    }

    /// <summary>
    /// Draws one frame of <paramref name="mode"/> onto <paramref name="segment"/> at
    /// <paramref name="now"/>.
    /// </summary>
    internal static void Render(EffectSegment segment, uint now, int mode)
    {
        ArgumentNullException.ThrowIfNull(segment);

        if (!IsReady)
        {
            return;
        }

        State state = segment.Scratch(() => new State());

        lock (Gate)
        {
            Prepare(segment.Length);

            // Everything the engine needs for THIS caller, because the engine's one segment is
            // shared and whoever rendered last left their own state in it. The previous frame
            // counts: anything that fades or trails reads the buffer back.
            Native.Length((ushort)segment.Length);

            for (int i = 0; i < segment.Length; i++)
            {
                RgbColor p = segment.Pixels[i];
                _scratchPixels[i] = ((uint)p.R << 16) | ((uint)p.G << 8) | p.B;
            }

            Native.PixelsSet(_scratchPixels, (ushort)segment.Length);

            Native.Segment(
                (byte)mode,
                PaletteFor(segment),
                segment.Speed,
                segment.Intensity,
                Pack(segment, 0),
                Pack(segment, 1),
                Pack(segment, 2));

            Native.Controls(
                segment.Custom1, segment.Custom2, segment.Custom3,
                (byte)(segment.Option1 ? 1 : 0),
                (byte)(segment.Option2 ? 1 : 0),
                (byte)(segment.Option3 ? 1 : 0));

            // The engine renders into bus order, so reverse is its business, not ours. Mirror is
            // not modelled here.
            Native.Orientation((byte)(segment.Reverse ? 1 : 0), 0);

            Native.RuntimeSet(
                (ushort)segment.Aux0, (ushort)segment.Aux1, segment.Step, segment.Call,
                state.Data, (ushort)state.Length);

            Native.Render(now, (ushort)segment.Length, _scratchPixels);

            Native.RuntimeGet(
                out ushort aux0, out ushort aux1, out uint step, out uint call,
                state.Data, (ushort)state.Data.Length, out ushort length);

            segment.Aux0 = aux0;
            segment.Aux1 = aux1;
            segment.Step = step;
            // Draw() counts the frame itself, so hand back one less than the engine reached or every
            // frame would count twice.
            segment.Call = call > 0 ? call - 1 : 0;
            state.Length = length <= state.Data.Length ? length : 0;

            for (int i = 0; i < segment.Length; i++)
            {
                uint pixel = _scratchPixels[i];
                segment.Pixels[i] = new RgbColor(
                    (byte)((pixel >> 16) & 0xFF),
                    (byte)((pixel >> 8) & 0xFF),
                    (byte)(pixel & 0xFF));
            }
        }
    }

    /// <summary>
    /// Sets the engine up once, at a capacity large enough for any caller.
    /// <para>
    /// Once, not once per length. Re-initialising resets the segment, and with many previews alive
    /// at once - each a different length - that would wipe one caller's state on every frame of
    /// another's. Length is set per render instead, through <c>wled_length</c>, which does not reset.
    /// </para>
    /// </summary>
    /// <summary>
    /// The palette id to hand the engine, which is not always the one the segment names.
    /// <para>
    /// Custom palettes live on the controller's filesystem, which is not here, so the engine has
    /// none of its own and a segment set to one would fall back to palette 0 - and palette 0 means
    /// "use the colour slot", so a palette-driven effect came out a flat wash of the primary colour,
    /// or black if the primary is black, while the house drew the palette properly. The app has the
    /// gradient, so it is pushed in and the id kept.
    /// </para>
    /// <para>
    /// Anything else out of the engine's range is refused rather than passed on. Ids from the
    /// palette count up to 245 index past the end of WLED's gradient table - 71 reads one past it
    /// and segfaults - and taking the whole process down is a poor way to draw a preview.
    /// </para>
    /// </summary>
    private static byte PaletteFor(EffectSegment segment)
    {
        int id = segment.PaletteId;

        // WLED addresses custom palettes as 255 minus the slot.
        if (id > 245 && segment.Palette is { IsGradient: true } gradient)
        {
            int slot = 255 - id;
            var entries = new uint[16];

            for (int i = 0; i < entries.Length; i++)
            {
                RgbColor c = gradient.ColorAt(
                    i / 15d, segment.Colors[0], segment.Colors[1], segment.Colors[2]);
                entries[i] = ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
            }

            Native.CustomPalette((byte)slot, entries);
            return (byte)id;
        }

        if (id < 0 || (id >= Native.PaletteCount() && id <= 245))
        {
            return 0;
        }

        return (byte)Math.Clamp(id, 0, 255);
    }

    private static void Prepare(int length)
    {
        if (_scratchPixels.Length < length)
        {
            _scratchPixels = new uint[length];
        }

        if (_pixelCount >= length)
        {
            return;
        }

        // 0 is WLED's unlimited frame rate, which is what both controllers here run, and it decides
        // FRAMETIME - a number several effects add to their own counters, so it is behaviour.
        _pixelCount = Math.Max(length, 1024);
        Native.Begin((ushort)_pixelCount, 0);
    }

    private static uint Pack(EffectSegment segment, int slot)
    {
        RgbColor c = slot < segment.Colors.Length ? segment.Colors[slot] : RgbColor.Black;
        return ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
    }

    private static bool TryLoad()
    {
        try
        {
            string? found = Candidates().FirstOrDefault(File.Exists);
            if (found is null)
            {
                Unavailable = "wledfx.dll not found - build it with `pwsh native/build.ps1`";
                return false;
            }

            nint handle = NativeLibrary.Load(Path.GetFullPath(found));
            NativeLibrary.SetDllImportResolver(
                typeof(NativeEngine).Assembly,
                (name, _, _) => name == Dll ? handle : 0);

            return Native.ModeCount() > 0;
        }
        catch (Exception e) when (e is DllNotFoundException or BadImageFormatException
                                      or EntryPointNotFoundException or IOException)
        {
            Unavailable = e.Message;
            return false;
        }
    }

    private static IEnumerable<string> Candidates()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "wledfx.dll");
        yield return Path.Combine(AppContext.BaseDirectory, "native", "wledfx.dll");
        yield return Path.Combine("native", "wledfx.dll");
        yield return Path.Combine("..", "..", "..", "..", "..", "native", "wledfx.dll");
    }

    /// <summary>WLED's per-segment data buffer, which effects allocate for themselves.</summary>
    private sealed class State
    {
        // uint16_t in the firmware, so this is the whole of what any effect can ask for.
        public byte[] Data { get; } = new byte[ushort.MaxValue];

        public int Length { get; set; }
    }

    private static class Native
    {
        [DllImport(Dll, EntryPoint = "wled_begin")]
        public static extern void Begin(ushort count, byte fps);

        [DllImport(Dll, EntryPoint = "wled_segment")]
        public static extern void Segment(byte fx, byte pal, byte speed, byte intensity,
                                          uint c0, uint c1, uint c2);

        [DllImport(Dll, EntryPoint = "wled_controls")]
        public static extern void Controls(byte custom1, byte custom2, byte custom3,
                                           byte check1, byte check2, byte check3);

        [DllImport(Dll, EntryPoint = "wled_orientation")]
        public static extern void Orientation(byte reverse, byte mirror);

        [DllImport(Dll, EntryPoint = "wled_length")]
        public static extern void Length(ushort length);

        [DllImport(Dll, EntryPoint = "wled_pixels_set")]
        public static extern void PixelsSet(uint[] pixels, ushort count);

        [DllImport(Dll, EntryPoint = "wled_runtime_set")]
        public static extern void RuntimeSet(ushort aux0, ushort aux1, uint step, uint call,
                                             byte[] data, ushort len);

        [DllImport(Dll, EntryPoint = "wled_runtime_get")]
        public static extern void RuntimeGet(out ushort aux0, out ushort aux1, out uint step,
                                             out uint call, byte[] data, ushort capacity,
                                             out ushort len);

        [DllImport(Dll, EntryPoint = "wled_render")]
        public static extern void Render(uint now, ushort count, uint[] pixels);

        [DllImport(Dll, EntryPoint = "wled_custom_palette")]
        public static extern void CustomPalette(byte slot, uint[] entries);

        [DllImport(Dll, EntryPoint = "wled_palette_count")]
        public static extern byte PaletteCount();

        [DllImport(Dll, EntryPoint = "wled_mode_count")]
        public static extern byte ModeCount();

        // The pointer is into the DLL's own static table, so .NET must not try to free it.
        [DllImport(Dll, EntryPoint = "wled_mode_data")]
        public static extern nint ModeDataPtr(byte id);
    }
}

/// <summary>One of WLED's effects, drawn by the firmware's own code through <see cref="NativeEngine"/>.</summary>
public sealed class NativeEffect(int mode, string name) : IWledEffect
{
    /// <summary>The effect's id in the engine's table, which is also the controller's id.</summary>
    public int Mode { get; } = mode;

    public string Name { get; } = name;

    public void Render(EffectSegment segment, uint now) => NativeEngine.Render(segment, now, Mode);
}
