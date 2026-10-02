// Drives a WLED controller from the command line, so a session does not need a browser to do it.
//
// Control is plain HTTP and always was - curl can do it. The one thing it cannot do is read the
// strip back: WLED's live preview is a WebSocket, and that is the only reason captures ever went
// through a browser pane. This covers both.
//
//   dotnet run tools/wled.cs -- off <host>
//   dotnet run tools/wled.cs -- state <host>
//   dotnet run tools/wled.cs -- set <host> seg=<n> fx=<n> sx=<n> ix=<n> pal=<n> c1=<n> ...
//   dotnet run tools/wled.cs -- capture <host> seg=<n> from=<led> to=<led> ms=<n> [settle=<n>] [fx=... ]
//   WLED_SWEEP_EFFECTS=0,1,2 WLED_SWEEP_INTO=<dir> dotnet run tools/wled.cs -- sweep <host> ...
//
// Capture prints one line of hex RGB triples per frame, in wire order, ready to check in as a
// fixture. It turns nothing on by itself: pass the same fx/sx/ix arguments as `set` to have it apply
// a state first, and it always leaves the lights as it found the switch - off - when it is done.

using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: off|state|set|capture|sweep <host> [key=value ...]");
    return 1;
}

string command = args[0];
string host = args[1];

Dictionary<string, int> options = new(StringComparer.OrdinalIgnoreCase);

// Colour slots are an array in WLED's JSON, not a number, so they do not go through `options` with
// everything else: col0=00ff00 col1=000000. Needed to compare a capture against a render, which has
// to be told the same colours.
Dictionary<int, string> colours = new();

// Booleans have to be sent as JSON booleans, not as 0 and 1. WLED reads them through getBoolVal,
// which is `elem | dflt`, and ArduinoJson's `|` is strictly typed: a number is not a bool, so the
// default wins and the field is silently left alone. That quietly discarded every rev=, mi= and
// o1/o2/o3= this tool was ever given - the effect option checkboxes among them.
Dictionary<string, bool> switches = new(StringComparer.OrdinalIgnoreCase);

foreach (string argument in args.Skip(2))
{
    string[] parts = argument.Split('=', 2);

    if (parts.Length != 2)
    {
        continue;
    }

    if (parts[0].Length == 4
        && parts[0].StartsWith("col", StringComparison.OrdinalIgnoreCase)
        && char.IsDigit(parts[0][3]))
    {
        colours[parts[0][3] - '0'] = parts[1];
    }
    else if (bool.TryParse(parts[1], out bool flag))
    {
        switches[parts[0]] = flag;
    }
    else if (int.TryParse(parts[1], CultureInfo.InvariantCulture, out int value))
    {
        options[parts[0]] = value;
    }
}

using var http = new HttpClient { BaseAddress = new Uri($"http://{host}/"), Timeout = TimeSpan.FromSeconds(10) };

switch (command)
{
    case "off":
        await AllOff();
        Console.WriteLine(await http.GetStringAsync("json/state"));
        return 0;

    case "state":
        Console.WriteLine(await http.GetStringAsync("json/state"));
        return 0;

    case "set":
        await Apply();
        return 0;

    case "capture":
        await Capture();
        return 0;

    case "sweep":
        await Sweep();
        return 0;

    default:
        Console.Error.WriteLine($"unknown command '{command}'");
        return 1;
}

/// <summary>Everything dark, every segment, which is how these controllers are meant to be left.</summary>
async Task AllOff()
{
    var segments = new JsonArray();

    for (int i = 0; i < 4; i++)
    {
        segments.Add(new JsonObject { ["id"] = i, ["on"] = false });
    }

    await Post(new JsonObject { ["on"] = false, ["seg"] = segments });
}

/// <summary>Sets one segment's effect from whatever key=value pairs were given.</summary>
async Task Apply()
{
    int id = options.TryGetValue("seg", out int chosen) ? chosen : 0;

    var segment = new JsonObject { ["id"] = id, ["on"] = true, ["bri"] = 255 };

    foreach ((string key, int value) in options)
    {
        if (key is "seg" or "ms" or "settle" or "from" or "to")
        {
            continue;
        }

        segment[key] = value;
    }

    foreach ((string key, bool flag) in switches)
    {
        segment[key] = JsonValue.Create(flag);
    }

    if (colours.Count > 0)
    {
        var slots = new JsonArray();

        for (int slot = 0; slot <= colours.Keys.Max(); slot++)
        {
            string hex = colours.TryGetValue(slot, out string? given) ? given : "000000";

            // JsonValue.Create rather than JsonArray's collection initialiser: a file-based app runs
            // with a source-generated resolver that has no metadata for a bare int.
            slots.Add(new JsonArray
            {
                JsonValue.Create(Convert.ToInt32(hex.Substring(0, 2), 16)),
                JsonValue.Create(Convert.ToInt32(hex.Substring(2, 2), 16)),
                JsonValue.Create(Convert.ToInt32(hex.Substring(4, 2), 16)),
            });
        }

        segment["col"] = slots;
    }

    // The other segments off, so what is measured is the one asked for and nothing else.
    var segments = new JsonArray();

    for (int i = 0; i < 4; i++)
    {
        segments.Add(i == id ? segment : new JsonObject { ["id"] = i, ["on"] = false });
    }

    await Post(new JsonObject
    {
        ["on"] = true,
        ["bri"] = 255,
        ["transition"] = 0,
        ["seg"] = segments,
    });
}

/// <summary>
/// Captures a list of effects in one go, each to its own file.
/// <para>
/// Only for capturing many: one process instead of one per effect, which matters when the list is
/// all 187 of them. Takes effects=<id>,<id>,... and into=<directory>, and writes <id>.txt per
/// effect. Everything else - the segment, the colours, the controls - comes from the same arguments
/// `capture` takes, and it leaves the lights off at the end exactly as `capture` does.
/// </para>
/// </summary>
async Task Sweep()
{
    string list = Environment.GetEnvironmentVariable("WLED_SWEEP_EFFECTS")
        ?? throw new InvalidOperationException("set WLED_SWEEP_EFFECTS to a comma-separated id list");
    string into = Environment.GetEnvironmentVariable("WLED_SWEEP_INTO")
        ?? throw new InvalidOperationException("set WLED_SWEEP_INTO to an output directory");

    Directory.CreateDirectory(into);
    // Entries are separated by ';' because an entry's own settings use ','.
    string[] effects = list.Split(';').Where(x => x.Length > 0).ToArray();

    for (int n = 0; n < effects.Length; n++)
    {
        // An entry may carry its own settings: "65:ix=112,c1=0,o1=true". Effects declare defaults in
        // their fxdata and WLED's UI applies them when you pick one, but the JSON API does not, so
        // they have to be sent explicitly or the two sides run the same effect differently set up.
        string[] parts = effects[n].Split(':', 2);
        int id = int.Parse(parts[0], CultureInfo.InvariantCulture);

        var restoreInts = new Dictionary<string, int>(options);
        var restoreFlags = new Dictionary<string, bool>(switches, StringComparer.OrdinalIgnoreCase);

        if (parts.Length == 2)
        {
            foreach (string pair in parts[1].Split(','))
            {
                string[] kv = pair.Split('=', 2);
                if (kv.Length != 2) continue;
                if (bool.TryParse(kv[1], out bool flag)) switches[kv[0]] = flag;
                else if (int.TryParse(kv[1], CultureInfo.InvariantCulture, out int value)) options[kv[0]] = value;
            }
        }

        options["fx"] = id;
        string[] lines = await Frames();
        await File.WriteAllLinesAsync(Path.Combine(into, $"{id}.txt"), lines);
        Console.Error.WriteLine($"[{n + 1}/{effects.Length}] fx {id}: {lines.Length} frames");

        options.Clear();
        foreach ((string k, int v) in restoreInts) options[k] = v;
        switches.Clear();
        foreach ((string k, bool v) in restoreFlags) switches[k] = v;
    }

    await AllOff();
}

/// <summary>Reads the strip back over the live preview socket, one hex line per frame.</summary>
async Task Capture()
{
    string[] lines = await Frames();
    await AllOff();

    Console.Error.WriteLine($"{lines.Length} frames, lights off");

    foreach (string line in lines)
    {
        Console.WriteLine(line);
    }
}

/// <summary>Applies the state if asked, then reads back one hex line per frame.</summary>
async Task<string[]> Frames()
{
    if (options.ContainsKey("fx"))
    {
        await Apply();
    }

    int settle = options.TryGetValue("settle", out int wait) ? wait : 1500;
    int duration = options.TryGetValue("ms", out int ms) ? ms : 10_000;
    int from = options.TryGetValue("from", out int first) ? first : 0;
    int to = options.TryGetValue("to", out int last) ? last : int.MaxValue;

    using var socket = new ClientWebSocket();
    await socket.ConnectAsync(new Uri($"ws://{host}/ws"), CancellationToken.None);

    await socket.SendAsync(
        Encoding.UTF8.GetBytes("{\"lv\":true}"), WebSocketMessageType.Text, true, CancellationToken.None);

    var buffer = new byte[16384];
    long started = Environment.TickCount64;
    var lines = new List<string>();

    while (Environment.TickCount64 - started < settle + duration)
    {
        WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, CancellationToken.None);

        if (result.MessageType != WebSocketMessageType.Binary || Environment.TickCount64 - started < settle)
        {
            continue;
        }

        // Two bytes of header, then three per LED.
        var text = new StringBuilder();
        int stop = Math.Min(to, ((result.Count - 2) / 3) - 1);

        for (int i = from; i <= stop; i++)
        {
            int at = 2 + (i * 3);
            text.Append(buffer[at].ToString("x2", CultureInfo.InvariantCulture));
            text.Append(buffer[at + 1].ToString("x2", CultureInfo.InvariantCulture));
            text.Append(buffer[at + 2].ToString("x2", CultureInfo.InvariantCulture));
        }

        lines.Add(text.ToString());
    }

    await socket.SendAsync(
        Encoding.UTF8.GetBytes("{\"lv\":false}"), WebSocketMessageType.Text, true, CancellationToken.None);

    // Turning the lights off is the caller's job now, because `sweep` has more to capture first and
    // the one rule here is that they are off when everything is done.
    return lines.ToArray();
}

async Task Post(JsonNode body)
{
    using var content = new StringContent(
        body.ToJsonString(new JsonSerializerOptions()), Encoding.UTF8, "application/json");

    using HttpResponseMessage response = await http.PostAsync("json/state", content);
    response.EnsureSuccessStatusCode();
}
