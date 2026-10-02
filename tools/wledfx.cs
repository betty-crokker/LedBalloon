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
// `ms` is the gap between the timestamps the engine is handed, and it takes a fraction: south
// renders at 95 to 100 frames a second while its live preview is streaming, so the interval is
// about 10.4 ms and rounding it to 10 drifts four percent a frame. That shows up as an effect whose
// motion is wrong when it is the measurement that is wrong - and it only shows up on the effects
// that advance per rendered frame rather than per millisecond.
//
// `fps` is WLED's own target, where 0 is the unlimited mode both controllers here use; it decides
// FRAMETIME, which many effects add to their own counters, so it is behaviour and not tuning.
double stepMs = options.TryGetValue("ms", out string? given)
    && double.TryParse(given, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
        ? parsed : 10.4;
int fps = Number("fps", 0);
int settle = Number("settle", 60);       // frames rendered and thrown away before the first printed one
int frames = Number("frames", 12);
int bpm = Number("sound", 120);
bool sound = flags.Contains("sound") || options.ContainsKey("sound");

// The three colour slots, as hex: col0=00ff00. The default is the green primary every comparison
// against the house has used, with the other two black.
uint Colour(string key, uint fallback) =>
    options.TryGetValue(key, out string? raw) && raw.Length == 6
        ? Convert.ToUInt32(raw, 16) : fallback;

uint colour0 = Colour("col0", 0x00FF00);
uint colour1 = Colour("col1", 0x000000);
uint colour2 = Colour("col2", 0x000000);

// The six controls behind the sliders. Several effects read nothing else, so a comparison against a
// controller has to set them on both sides or it is comparing two different effects.
int custom1 = Number("c1", 128);
int custom2 = Number("c2", 128);
int custom3 = Number("c3", 16);
int check1 = Number("o1", 0);
int check2 = Number("o2", 0);
int check3 = Number("o3", 0);

// How the segment is wired. South's roofline runs from the far end and renders with rev set, and
// one effect - Flow - reads it and applies it per zone, so it cannot be imitated afterwards by
// reversing the output.
int reverse = Number("rev", 0);
int mirror = Number("mi", 0);

// Where the clock starts, in milliseconds. Normally 0, because an effect's own counters restart when
// it does. But a few effects are not functions of elapsed time alone: Pacifica computes
// strip.now = (strip.now>>2) + ((strip.now * speed)>>7), and that multiply wraps at 32 bits, so what
// it draws depends on the controller's absolute uptime. To compare one against a controller that has
// been up for days, the clock has to start where that controller's clock is.
long clockFrom = options.TryGetValue("at", out string? from)
    && long.TryParse(from, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsedFrom)
        ? parsedFrom : 0;

Engine.Begin((ushort)leds, (byte)fps);

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

        string[] lines = Render(mode, palette, speed, intensity, stepMs, settle, frames, sound, bpm, leds, clockFrom);

        Console.Error.WriteLine(
            $"{names[mode]} (fx {mode}), palette {palette}, sx {speed}, ix {intensity}, " +
            $"colours {colour0:x6}/{colour1:x6}/{colour2:x6}, " +
            $"c1 {custom1} c2 {custom2} c3 {custom3} o1 {check1} o2 {check2} o3 {check3}, " +
            $"rev {reverse} mi {mirror}, clock from {clockFrom} ms, " +
            $"{frames} frames of {stepMs:F2} ms at fps {fps} after {settle} settling{(sound ? $", sound at {bpm} bpm" : "")}");

        foreach (string line in lines) Console.WriteLine(line);
        return 0;
    }

    // Renders many effects in one process, each to its own file, applying each effect's own fxdata
    // defaults. One process instead of 187 of them, which is the difference between minutes and an
    // hour when comparing the whole list against the house.
    case "dump":
    {
        string into = Environment.GetEnvironmentVariable("WLEDFX_DUMP_INTO")
            ?? throw new InvalidOperationException("set WLEDFX_DUMP_INTO");
        Directory.CreateDirectory(into);

        // Per-effect clock origins, "<id><tab><ms>" a line. Several effects are functions of the
        // absolute clock rather than of elapsed time - color_wipe takes strip.now modulo a cycle
        // that is 19.8 seconds long at speed 128 - so an engine whose clock starts at zero can only
        // ever show the first slice of that cycle, and no amount of searching will reach the
        // controller's phase. Given where the controller's clock actually was, it matches.
        var origins = new Dictionary<int, long>();
        if (Environment.GetEnvironmentVariable("WLEDFX_DUMP_CLOCKS") is string clockFile
            && File.Exists(clockFile))
        {
            foreach (string line in File.ReadLines(clockFile))
            {
                string[] bits = line.Split('	');
                if (bits.Length == 2
                    && int.TryParse(bits[0], out int which)
                    && long.TryParse(bits[1], out long at))
                {
                    origins[which] = at;
                }
            }
        }

        int done = 0;
        for (int mode = 0; mode < names.Length; mode++)
        {
            Defaults(mode, out int useSx, out int useIx, out int usePal,
                     out int c1, out int c2, out int c3, out int o1, out int o2, out int o3);

            Engine.Begin((ushort)leds, (byte)fps);
            Engine.Segment((byte)mode, (byte)usePal, (byte)useSx, (byte)useIx,
                           colour0, colour1, colour2);
            Engine.Controls((byte)c1, (byte)c2, (byte)c3, (byte)o1, (byte)o2, (byte)o3);
            Engine.Orientation((byte)reverse, (byte)mirror);

            var output = new uint[leds];
            var lines = new List<string>(frames);
            double clock = origins.TryGetValue(mode, out long origin) ? origin : clockFrom;

            for (int frame = 0; frame < settle + frames; frame++)
            {
                uint at = (uint)Math.Round(clock);
                if (sound) Sound(at, bpm);
                Engine.Frame(at, (ushort)leds, output);
                clock += stepMs;
                if (frame < settle) continue;

                var text = new StringBuilder(leds * 6);
                foreach (uint pixel in output)
                {
                    text.Append(((pixel >> 16) & 0xFF).ToString("x2", CultureInfo.InvariantCulture));
                    text.Append(((pixel >> 8) & 0xFF).ToString("x2", CultureInfo.InvariantCulture));
                    text.Append((pixel & 0xFF).ToString("x2", CultureInfo.InvariantCulture));
                }
                lines.Add(text.ToString());
            }

            File.WriteAllLines(Path.Combine(into, $"{mode}.txt"), lines);
            Console.WriteLine($"{mode}	{names[mode]}	{usePal}	{useSx}	{useIx}	" +
                              $"{c1},{c2},{c3},{o1},{o2},{o3}");
            done++;
        }

        Console.Error.WriteLine($"rendered {done} effects into {into}");
        return 0;
    }

    case "sweep":
    {
        Console.WriteLine($"{"fx",3}  {"name",-22} {"lit",4} {"distinct",8} {"moving",6} {"mean",5}");

        for (int mode = 0; mode < names.Length; mode++)
        {
            string[] lines = Render(mode, palette, speed, intensity, stepMs, settle, 2, sound, bpm, leds, clockFrom);
            (int lit, int distinct, int moving, double mean) = Measure(lines);
            Console.WriteLine($"{mode,3}  {names[mode],-22} {lit,4} {distinct,8} {moving,6} {mean,5:F1}");
        }

        return 0;
    }

    default:
        Console.Error.WriteLine($"unknown command '{args[0]}'");
        return 1;
}

// What an effect's own fxdata asks for, falling back to this run's arguments. Section 5 of the
// fxdata is the default list - Palette's is "ix=112,c1=0,o1=1,o2=0,o3=1", and without it Palette
// renders one colour and does not move.
void Defaults(int mode, out int useSx, out int useIx, out int usePal,
              out int c1, out int c2, out int c3, out int o1, out int o2, out int o3)
{
    useSx = speed; useIx = intensity; usePal = palette;
    c1 = custom1; c2 = custom2; c3 = custom3; o1 = check1; o2 = check2; o3 = check3;

    string data = Engine.ModeData((byte)mode);
    string[] parts = data.Split(';');
    if (parts.Length < 5) return;

    foreach (string pair in parts[4].Split(','))
    {
        string[] kv = pair.Split('=', 2);
        if (kv.Length != 2 || !int.TryParse(kv[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
        {
            continue;
        }
        switch (kv[0])
        {
            case "sx": useSx = v; break;
            case "ix": useIx = v; break;
            case "pal": usePal = v; break;
            case "c1": c1 = v; break;
            case "c2": c2 = v; break;
            case "c3": c3 = v; break;
            case "o1": o1 = v; break;
            case "o2": o2 = v; break;
            case "o3": o3 = v; break;
        }
    }
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

string[] Render(int mode, int pal, int sx, int ix, double ms, int warm, int count,
                bool withSound, int beats, int pixels, long from)
{
    // Each render starts the clock at zero, because the clock an effect sees restarts when the
    // effect does - an effect that waits before its first change sits black until it has.
    Engine.Begin((ushort)pixels, (byte)fps);
    Engine.Segment((byte)mode, (byte)pal, (byte)sx, (byte)ix, colour0, colour1, colour2);
    Engine.Controls((byte)custom1, (byte)custom2, (byte)custom3,
                    (byte)check1, (byte)check2, (byte)check3);
    Engine.Orientation((byte)reverse, (byte)mirror);

    var output = new uint[pixels];
    var lines = new List<string>(count);
    double clock = from;

    for (int frame = 0; frame < warm + count; frame++)
    {
        // The clock is accumulated as a fraction and rounded only when handed over, so a step of
        // 10.4 ms does not quietly become 10.
        uint now = (uint)Math.Round(clock);
        if (withSound) Sound(now, beats);
        Engine.Frame(now, (ushort)pixels, output);
        clock += ms;

        // WLEDFX_CLOCK=1 prints the clock the effects are actually seeing. Worth keeping: this
        // repository has been caught out by frame time more than once, and when an effect holds
        // still the first suspicion is always that its clock has stopped. Here it ruled that out in
        // one run - strip.now was advancing fine and the bug was in map().
        if (Environment.GetEnvironmentVariable("WLEDFX_CLOCK") is not null && frame < 6)
        {
            Console.Error.WriteLine($"  frame {frame}: told {now} ms, engine clock {Engine.Now()} ms");
        }

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
    public static extern void Begin(ushort count, byte fps);

    [DllImport(Dll, EntryPoint = "wled_segment")]
    public static extern void Segment(byte fx, byte pal, byte speed, byte intensity,
                                      uint c0, uint c1, uint c2);

    [DllImport(Dll, EntryPoint = "wled_controls")]
    public static extern void Controls(byte custom1, byte custom2, byte custom3,
                                       byte check1, byte check2, byte check3);

    [DllImport(Dll, EntryPoint = "wled_frame")]
    public static extern void Frame(uint now, ushort count, [Out] uint[] pixels);

    [DllImport(Dll, EntryPoint = "wled_audio")]
    public static extern void Audio(float volume, byte[] bins, float majorPeakHz,
                                    float magnitude, byte beat);

    [DllImport(Dll, EntryPoint = "wled_orientation")]
    public static extern void Orientation(byte reverse, byte mirror);

    [DllImport(Dll, EntryPoint = "wled_now")]
    public static extern uint Now();

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
