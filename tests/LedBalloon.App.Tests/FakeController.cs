using System.Net;
using System.Linq;
using System.Text;
using LedBalloon.Core.Layout;

namespace LedBalloon.App.Tests;

/// <summary>
/// A WLED controller that exists only for the length of a test: one HTTP endpoint on the loopback
/// address, answering the single request connecting to a controller makes.
/// <para>
/// Connecting asks for <c>/json</c>, which is state, info, effect names and palette names in one
/// document, and then for the preset file - which a controller with no presets saved does not serve,
/// so a 404 is a normal answer rather than a failure. It also opens a WebSocket, which nothing here
/// answers; that fails in the background and is reported rather than thrown, which is the same thing
/// that happens when a real controller drops off the network.
/// </para>
/// <para>
/// The segment it reports carries an effect, a palette, both sliders and its colors, because a real
/// one always does. It used to be bounds and nothing else, which is a state no controller is ever
/// in, and the difference showed: putting a segment back the way it was is a patch, and a patch
/// cannot set a field back to "not mentioned" - so an undo that should have been ordinary looked
/// broken against a fake that had left the fields out in the first place.
/// </para>
/// </summary>
internal sealed class FakeController : IDisposable
{
    /// <summary>What the controller calls itself, and so what anything keyed by controller uses.</summary>
    public const string Key = "aa:bb:cc:dd:ee:ff";

    /// <summary>
    /// This one's MAC, which is how the project tells two controllers apart.
    /// </summary>
    /// <remarks>
    /// A second stand-in needs a second identity or the house has one controller twice, and a
    /// palette cannot travel from a box to itself.
    /// </remarks>
    public string DeviceKey { get; private init; } = Key;

    private readonly List<string> _posted = [];
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _serving;

    private FakeController(HttpListener listener, string host, string document, string fxdata)
    {
        _listener = listener;
        Host = host;
        _serving = Task.Run(() => ServeAsync(document, fxdata));
    }

    /// <summary>Where to point a device at, as the app would be given it.</summary>
    public string Host { get; }

    /// <summary>
    /// Every state patch the app has posted, in order, as the JSON that went over the wire.
    /// <para>
    /// Kept so a test can say what did not happen as well as what did. Locked because the listener
    /// answers on its own thread and the test reads from the UI one.
    /// </para>
    /// </summary>
    public IReadOnlyList<string> Posts
    {
        get
        {
            lock (_posted)
            {
                return [.. _posted];
            }
        }
    }

    /// <summary>
    /// Waits for the app to post something, up to <paramref name="within"/>.
    /// </summary>
    /// <remarks>
    /// Posts are paced by the coalescer rather than sent as they are made, so there is always a
    /// wait; asserting straight after the edit would pass whatever the code did.
    /// </remarks>
    public async Task<bool> WaitForPostAsync(TimeSpan within)
    {
        DateTime until = DateTime.UtcNow + within;

        while (DateTime.UtcNow < until)
        {
            if (Posts.Count > 0)
            {
                return true;
            }

            await Task.Delay(20);
        }

        return false;
    }

    public static FakeController Start(params string[] effects) => Start(effects, null);

    /// <summary>
    /// A controller that also answers for <c>/json/fxdata</c>, one entry per effect.
    /// </summary>
    /// <remarks>
    /// The metadata decides real things - which sliders an effect gets, and whether it is offered at
    /// all, since the ones that need a matrix cannot run on a strip. A fake that 404s it leaves
    /// every effect looking like one nothing is known about.
    /// </remarks>
    /// <summary>
    /// The custom palette files this controller holds, keyed by name, lowest slot first.
    /// </summary>
    /// <remarks>
    /// Here because a palette made on one controller and wanted on the other has to travel, and the
    /// travelling is the part nothing could reach: the picker offers the rest of the house's
    /// palettes, writes the file when one is picked, and uses the id it lands on - which is not the
    /// id it had, since custom palettes are numbered by position in each controller's own list.
    /// </remarks>
    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);

    /// <summary>Where each upload was posted, since /edit and /upload are not interchangeable.</summary>
    public List<string> Uploads { get; } = [];

    /// <summary>
    /// Which file each upload was for, in order, so a test can count writes rather than only see
    /// the last one. Overwriting a file with the same bytes leaves no trace in <see cref="Files"/>,
    /// and "was this written again" is exactly the question a save that skips unchanged work raises.
    /// </summary>
    public List<string> Written { get; } = [];

    /// <summary>
    /// This one's configuration, which is where its timetable lives.
    /// <para>
    /// Held rather than fixed, because writing a timetable reads the whole configuration and
    /// posts it back with only the timers changed - a partial post is not safe on 0.15.3 - so a
    /// stand-in that cannot be read back cannot be written to either. The hw.led.fps key is here
    /// for the same reason: the real handler reads it unconditionally, and a document without it
    /// uncaps the frame rate.
    /// </para>
    /// </summary>
    public string Configuration { get; set; } =
        """{"hw":{"led":{"fps":42,"total":10}},"timers":{"cntdwn":{"en":false},"ins":[]}}""";

    /// <summary>Puts a custom palette on this controller, as if somebody had made it here.</summary>
    public void Holds(params string[] palettes)
    {
        for (int slot = 0; slot < palettes.Length; slot++)
        {
            Files[$"palette{slot}.json"] = Encoding.UTF8.GetBytes(palettes[slot]);
        }
    }

    public static FakeController Start(string[] effects, string[]? fxdata) =>
        Start(effects, fxdata, null);

    /// <summary>
    /// A controller that also reports a usermod block, which is where sound lives.
    /// </summary>
    /// <param name="usermods">
    /// The <c>u</c> object of <c>/json/info</c>, verbatim. A sound-reactive build describes what it
    /// is listening to here, and two dozen of the effects it offers do nothing without it.
    /// </param>
    public static FakeController Start(string[] effects, string[]? fxdata, string? usermods) =>
        Start(effects, fxdata, usermods, Key);

    /// <summary>A controller with an identity of its own, for a house that has two of them.</summary>
    public static FakeController Start(
        string[] effects, string[]? fxdata, string? usermods, string key)
    {
        // A port the operating system picks, so tests can run beside each other and beside anything
        // else already listening.
        var listener = new HttpListener();
        int port = FreePort();

        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        string names = string.Join(",", effects.Select(e => $"\"{e}\""));

        string document =
            $$"""
            {
              "state": { "on": false, "bri": 128, "seg": [ { "id": 0, "start": 0, "stop": 10, "len": 10, "on": true, "bri": 255, "fx": 0, "sx": 128, "ix": 128, "pal": 0, "col": [[255,160,0],[0,0,0],[0,0,0]] } ] },
              "info": { "name": "Fake", "ver": "0.15.3", "mac": "{{key}}", "leds": { "count": 10, "fps": 0 }{{Heard(usermods)}} },
              "effects": [ {{names}} ],
              "palettes": [ "Default", "* Random Cycle", "* Color 1", "* Colors 1&2",
                            "* Color Gradient", "* Colors Only", "Ocean", "Analogous" ]
            }
            """;

        string metadata = fxdata is null
            ? "[]"
            : "[" + string.Join(",", fxdata.Select(d => $"\"{d}\"")) + "]";

        return new FakeController(listener, $"127.0.0.1:{port}", document, metadata)
        {
            DeviceKey = key,
        };
    }

    /// <summary>The usermod block as an <c>info</c> member, or nothing at all when there is none.</summary>
    private static string Heard(string? usermods) =>
        usermods is { Length: > 0 } said ? $", \"u\": {said}" : string.Empty;

    /// <summary>
    /// Whatever file has been written here, if this is a request for one.
    /// <para>
    /// Anything, not just the palettes it began as: a write to this filesystem is verified by
    /// reading the file back, so a stand-in that accepts an upload and then will not serve it
    /// reports every write as having failed.
    /// </para>
    /// </summary>
    private byte[]? Held(string path)
    {
        string name = path.TrimStart('/');

        lock (_posted)
        {
            return Files.TryGetValue(name, out byte[]? file) ? file : null;
        }
    }

    /// <summary>
    /// What <c>/json/palx</c> says, which is the firmware's expansion of those files.
    /// </summary>
    /// <remarks>
    /// The custom ones, plus the handful WLED builds out of the segment's own color slots. The rest
    /// of the built-in gradients are not what any of this is about, and a palette the controller
    /// does not hold is exactly the thing that has to be absent here.
    /// </remarks>
    private string Expanded()
    {
        // WLED gives these as placeholders rather than colors: "c1" is the segment's first slot.
        // They are here because a swatch drawn from them has to follow the color boxes, and nothing
        // else in this fake would show that.
        var entries = new List<string>
        {
            "\"2\":[\"c1\"]",
            "\"3\":[\"c1\",\"c1\",\"c2\",\"c2\"]",
            "\"4\":[\"c3\",\"c2\",\"c1\"]",
        };

        lock (_posted)
        {
            for (int slot = 0; slot < 10; slot++)
            {
                if (!Files.TryGetValue($"palette{slot}.json", out byte[]? file))
                {
                    // WLED stops at the first gap, and so does this.
                    break;
                }

                string stops = string.Join(",", CustomPaletteFile.Parse(file)
                    .Select(stop => $"[{stop.Position},{stop.Color.R},{stop.Color.G},{stop.Color.B}]"));

                entries.Add($"\"{255 - slot}\":[{stops}]");
            }
        }

        return "{\"m\":0,\"p\":{" + string.Join(",", entries) + "}}";
    }

    private static int FreePort()
    {
        using var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);

        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        return port;
    }

    private async Task ServeAsync(string document, string fxdata)
    {
        while (!_stopping.IsCancellationRequested)
        {
            HttpListenerContext context;

            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
            {
                return;
            }

            string path = context.Request.Url?.AbsolutePath ?? "/";

            if (context.Request.HttpMethod == "POST")
            {
                using var body = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                string sent = await body.ReadToEndAsync();

                // A file upload rather than a state change. The part's filename says where it goes,
                // and the payload sits between the blank line after the headers and the closing
                // boundary - good enough for a stand-in.
                int at = sent.IndexOf("filename=\"/", StringComparison.Ordinal);

                if (at >= 0)
                {
                    int from = at + 11;
                    string name = sent[from..sent.IndexOf('"', from)];

                    int start = sent.IndexOf("\r\n\r\n", at, StringComparison.Ordinal) + 4;
                    int end = sent.LastIndexOf("\r\n--", StringComparison.Ordinal);

                    lock (_posted)
                    {
                        Uploads.Add(path.TrimStart('/'));
                        Written.Add(name.TrimStart('/'));
                        Files[name] = Encoding.UTF8.GetBytes(sent[start..end]);
                    }
                }
                else
                {
                    lock (_posted)
                    {
                        _posted.Add(sent);

                        // A configuration post replaces the configuration, the way the real one
                        // does, so a test can read back what it wrote rather than only that it
                        // wrote something.
                        if (path.TrimEnd('/') is "/json/cfg")
                        {
                            Configuration = sent;
                        }
                    }
                }

                context.Response.ContentType = "application/json";
                context.Response.Close();
                continue;
            }

            // The custom palette files, which is what a palette actually is. Everything else about
            // one - its id, its gradient - is worked out from where the file sits.
            if (Held(path) is { } file)
            {
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = file.Length;
                await context.Response.OutputStream.WriteAsync(file);

                context.Response.Close();
                continue;
            }

            if (path.TrimEnd('/') is "/cfg.json" or "/json/cfg")
            {
                byte[] body = Encoding.UTF8.GetBytes(Configuration);

                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = body.Length;
                await context.Response.OutputStream.WriteAsync(body);

                context.Response.Close();
                continue;
            }

            if (path.TrimEnd('/') is "/json/palx")
            {
                byte[] body = Encoding.UTF8.GetBytes(Expanded());

                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = body.Length;
                await context.Response.OutputStream.WriteAsync(body);

                context.Response.Close();
                continue;
            }

            if (path.TrimEnd('/') is "/json/fxdata" && fxdata != "[]")
            {
                byte[] body = Encoding.UTF8.GetBytes(fxdata);

                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = body.Length;
                await context.Response.OutputStream.WriteAsync(body);

                context.Response.Close();
                continue;
            }

            if (path.TrimEnd('/') is "/json")
            {
                byte[] body = Encoding.UTF8.GetBytes(document);

                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = body.Length;
                await context.Response.OutputStream.WriteAsync(body);
            }
            else
            {
                // Including the preset file, which a controller with nothing saved does not have.
                context.Response.StatusCode = 404;
            }

            context.Response.Close();
        }
    }

    public void Dispose()
    {
        _stopping.Cancel();
        _listener.Stop();
        _listener.Close();

        try
        {
            _serving.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Shutting down; whatever the loop was doing no longer matters.
        }

        _stopping.Dispose();
    }
}
