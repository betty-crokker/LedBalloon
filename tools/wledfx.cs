// Runs WLED's own effect engine on this machine, through native/wledfx.dll.
//
// This is the other half of tools/wled.cs. That one asks the house what it is showing; this one asks
// the firmware's own C++ the same question without a controller, a network or a strip. Both print
// one line of hex RGB triples per frame in wire order, so a render here and a capture there go
// through the same comparison unchanged.
//
//   dotnet run tools/wledfx.cs -- list
//   dotnet run tools/wledfx.cs -- render fx=Colorwaves pal=11 sx=128 ix=128
//   dotnet run tools/wledfx.cs -- sweep [pal=11] [sound]
//
// Build the DLL first:  pwsh native/build.ps1
//
// Effects are named, not numbered, because WLED's ids move between releases - see CLAUDE.md. `list`
// prints what this engine actually registered, which is the only authority on either.
//
// On frame time: `ms` is the gap between the timestamps the engine is handed, and it defaults to 23
// because that is FRAMETIME_FIXED and it is what the Colorwaves comparison against the house was
// measured at. It is not the 9 ms the house renders at, nor the 2 ms its FRAMETIME macro computes.
// Those three numbers are all different and using one where another belongs is a silent error.

using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: list|render|sweep [key=value ...]");
    return 1;
}

Engine.Load();

var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

foreach (string arg in args.Skip(1))
{
    int split = arg.IndexOf('=');
    if (split < 0) flags.Add(arg);
    else options[arg[..split]] = arg[(split + 1)..];
}

int Number(string key, int fallback) =>
    options.TryGetValue(key, out string? raw) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
        ? value : fallback;

int leds = Number("leds", 285);          // south's roofline: LEDs 25-309 of 310
int palette = Number("pal", 11);
int speed = Number("sx", 128);
int intensity = Number("ix", 128);
int stepMs = Number("ms", 23);
int settle = Number("settle", 60);       // frames rendered and thrown away before the first printed one
int frames = Number("frames", 12);
int bpm = Number("sound", 120);
bool sound = flags.Contains("sound") || options.ContainsKey("sound");

Engine.Begin((ushort)leds);

var names = new string[Engine.ModeCount()];
for (int i = 0; i < names.Length; i++)
{
    string data = Engine.ModeData((byte)i);
    int at = data.IndexOf('@');
    names[i] = at < 0 ? data : data[..at];
}

switch (args[0])
{
    case "list":
        Console.WriteLine($"{names.Length} effects, {Engine.PaletteCount()} palettes");
        for (int i = 0; i < names.Length; i++)
        {
            string data = Engine.ModeData((byte)i);
            int at = data.IndexOf('@');
            Console.WriteLine($"{i,3}  {names[i],-22}{(at < 0 ? "" : data[(at + 1)..])}");
        }
        return 0;

    case "render":
    {
        int mode = Mode(options.TryGetValue("fx", out string? wanted) ? wanted : "Colorwaves");
        if (mode < 0) return 1;

        string[] lines = Render(mode, palette, speed, intensity, stepMs, settle, frames, sound, bpm, leds);

        Console.Error.WriteLine(
            $"{names[mode]} (fx {mode}), palette {palette}, sx {speed}, ix {intensity}, " +
            $"{frames} frames of {stepMs} ms after {settle} settling{(sound ? $", sound at {bpm} bpm" : "")}");

        foreach (string line in lines) Console.WriteLine(line);
        return 0;
    }

    case "sweep":
    {
        Console.WriteLine($"{"fx",3}  {"name",-22} {"lit",4} {"distinct",8} {"moving",6} {"mean",5}");

        for (int mode = 0; mode < names.Length; mode++)
        {
            string[] lines = Render(mode, palette, speed, intensity, stepMs, settle, 2, sound, bpm, leds);
            (int lit, int distinct, int moving, double mean) = Measure(lines);
            Console.WriteLine($"{mode,3}  {names[mode],-22} {lit,4} {distinct,8} {moving,6} {mean,5:F1}");
        }

        return 0;
    }

    default:
        Console.Error.WriteLine($"unknown command '{args[0]}'");
        return 1;
}

// Accepts a name or a number, and prefers the name: WLED's ids move between releases.
int Mode(string wanted)
{
    int exact = Array.FindIndex(names, n => n.Equals(wanted, StringComparison.OrdinalIgnoreCase));
    if (exact >= 0) return exact;

    if (int.TryParse(wanted, out int number) && number >= 0 && number < names.Length) return number;

    string[] near = names
        .Where(n => n.Contains(wanted, StringComparison.OrdinalIgnoreCase))
        .Distinct()
        .ToArray();

    Console.Error.WriteLine($"no effect named '{wanted}'." +
        (near.Length > 0 ? $" did you mean: {string.Join(", ", near)}" : " try `list`"));
    return -1;
}

string[] Render(int mode, int pal, int sx, int ix, int ms, int warm, int count,
                bool withSound, int beats, int pixels)
{
    // Each render starts the clock at zero, because the clock an effect sees restarts when the
    // effect does - an effect that waits before its first change sits black until it has.
    Engine.Begin((ushort)pixels);
    Engine.Segment((byte)mode, (byte)pal, (byte)sx, (byte)ix, 0x00FF00, 0, 0);

    var output = new uint[pixels];
    var lines = new List<string>(count);
    uint now = 0;

    for (int frame = 0; frame < warm + count; frame++)
    {
        if (withSound) Sound(now, beats);
        Engine.Frame(now, (ushort)pixels, output);
        now += (uint)ms;

        if (frame < warm) continue;

        var text = new StringBuilder(pixels * 6);
        foreach (uint pixel in output)
        {
            text.Append(((pixel >> 16) & 0xFF).ToString("x2", CultureInfo.InvariantCulture));
            text.Append(((pixel >> 8) & 0xFF).ToString("x2", CultureInfo.InvariantCulture));
            text.Append((pixel & 0xFF).ToString("x2", CultureInfo.InvariantCulture));
        }
        lines.Add(text.ToString());
    }

    return lines.ToArray();
}

// LedBalloon.Core's SyntheticAudio, in the shape the engine's nine audio slots want. Neither
// controller here has a microphone, so this is the only way the sound-reactive effects can be seen
// at all - and being a pure function of the clock, it is reproducible in a way a microphone is not.
void Sound(uint nowMs, int beats)
{
    double at = nowMs / 1000.0;
    double beat = 60.0 / beats;
    double into = at % beat;
    double decay = Math.Exp(-6.0 * into / beat);
    bool hat = (int)(at / beat) % 2 == 1;

    var bins = new byte[16];
    for (int i = 0; i < bins.Length; i++)
    {
        double low = Math.Exp(-0.6 * i) * 255 * decay;
        double mid = Math.Exp(-((i - 5.0) * (i - 5.0)) / 4.0) * 160 * decay;
        double high = hat ? Math.Max(0, i - 9) * 28 : 0;
        bins[i] = (byte)Math.Clamp(low + mid + high, 0, 255);
    }

    double[] bassLine = [110.0, 146.8, 164.8, 196.0];
    double peak = bassLine[(int)(at / beat) % 4] * (1 + (0.5 * decay));

    Engine.Audio((float)Math.Min(255, 40 + (190 * decay)), bins, (float)peak,
                 (float)((2000 * decay) + 200), (byte)(into < 0.05 ? 1 : 0));
}

static (int Lit, int Distinct, int Moving, double Mean) Measure(string[] lines)
{
    string last = lines[^1];
    var pixels = new List<(int R, int G, int B)>();

    for (int at = 0; at + 6 <= last.Length; at += 6)
    {
        pixels.Add((Convert.ToInt32(last.Substring(at, 2), 16),
                    Convert.ToInt32(last.Substring(at + 2, 2), 16),
                    Convert.ToInt32(last.Substring(at + 4, 2), 16)));
    }

    int moving = 0;
    if (lines.Length > 1)
    {
        string previous = lines[^2];
        for (int at = 0; at + 6 <= last.Length; at += 6)
        {
            if (last.Substring(at, 6) != previous.Substring(at, 6)) moving++;
        }
    }

    return (pixels.Count(p => Math.Max(p.R, Math.Max(p.G, p.B)) > 0),
            pixels.Distinct().Count(),
            moving,
            pixels.Count == 0 ? 0 : pixels.Average(p => Math.Max(p.R, Math.Max(p.G, p.B))));
}

static class Engine
{
    private const string Dll = "wledfx";

    // Found relative to the repository rather than the working directory, so the tool works from
    // wherever it is run.
    public static void Load()
    {
        string[] candidates =
        [
            Path.Combine("native", "wledfx.dll"),
            Path.Combine("..", "native", "wledfx.dll"),
            Path.Combine(AppContext.BaseDirectory, "native", "wledfx.dll"),
        ];

        string? found = candidates.FirstOrDefault(File.Exists);
        if (found is null)
        {
            Console.Error.WriteLine("native/wledfx.dll not found - build it with `pwsh native/build.ps1`");
            Environment.Exit(1);
        }

        nint handle = NativeLibrary.Load(Path.GetFullPath(found));
        NativeLibrary.SetDllImportResolver(
            Assembly.GetExecutingAssembly(),
            (name, _, _) => name == Dll ? handle : 0);
    }

    [DllImport(Dll, EntryPoint = "wled_begin")]
    public static extern void Begin(ushort count);

    [DllImport(Dll, EntryPoint = "wled_segment")]
    public static extern void Segment(byte fx, byte pal, byte speed, byte intensity,
                                      uint c0, uint c1, uint c2);

    [DllImport(Dll, EntryPoint = "wled_frame")]
    public static extern void Frame(uint now, ushort count, [Out] uint[] pixels);

    [DllImport(Dll, EntryPoint = "wled_audio")]
    public static extern void Audio(float volume, byte[] bins, float majorPeakHz,
                                    float magnitude, byte beat);

    [DllImport(Dll, EntryPoint = "wled_mode_count")]
    public static extern byte ModeCount();

    [DllImport(Dll, EntryPoint = "wled_palette_count")]
    public static extern byte PaletteCount();

    // The pointer is into the DLL's own static table, so .NET must not try to free it: take the
    // pointer and copy the string out by hand.
    [DllImport(Dll, EntryPoint = "wled_mode_data")]
    private static extern nint ModeDataPtr(byte id);

    public static string ModeData(byte id) => Marshal.PtrToStringUTF8(ModeDataPtr(id)) ?? "";
}
