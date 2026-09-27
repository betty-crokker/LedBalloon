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
    Console.Error.WriteLine("usage: off|state|set|capture <host> [key=value ...]");
    return 1;
}

string command = args[0];
string host = args[1];

Dictionary<string, int> options = new(StringComparer.OrdinalIgnoreCase);

foreach (string argument in args.Skip(2))
{
    string[] parts = argument.Split('=', 2);

    if (parts.Length == 2 && int.TryParse(parts[1], CultureInfo.InvariantCulture, out int value))
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

/// <summary>Reads the strip back over the live preview socket, one hex line per frame.</summary>
async Task Capture()
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

    await AllOff();

    Console.Error.WriteLine($"{lines.Count} frames over {duration} ms, lights off");

    foreach (string line in lines)
    {
        Console.WriteLine(line);
    }
}

async Task Post(JsonNode body)
{
    using var content = new StringContent(
        body.ToJsonString(new JsonSerializerOptions()), Encoding.UTF8, "application/json");

    using HttpResponseMessage response = await http.PostAsync("json/state", content);
    response.EnsureSuccessStatusCode();
}
