using System.Net;
using System.Linq;
using System.Text;

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
    public static FakeController Start(string[] effects, string[]? fxdata) =>
        Start(effects, fxdata, null);

    /// <summary>
    /// A controller that also reports a usermod block, which is where sound lives.
    /// </summary>
    /// <param name="usermods">
    /// The <c>u</c> object of <c>/json/info</c>, verbatim. A sound-reactive build describes what it
    /// is listening to here, and two dozen of the effects it offers do nothing without it.
    /// </param>
    public static FakeController Start(string[] effects, string[]? fxdata, string? usermods)
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
              "info": { "name": "Fake", "ver": "0.15.3", "mac": "{{Key}}", "leds": { "count": 10, "fps": 0 }{{Heard(usermods)}} },
              "effects": [ {{names}} ],
              "palettes": [ "Default", "* Random Cycle", "* Color 1", "* Colors 1&2",
                            "* Color Gradient", "* Colors Only", "Ocean", "Analogous" ]
            }
            """;

        string metadata = fxdata is null
            ? "[]"
            : "[" + string.Join(",", fxdata.Select(d => $"\"{d}\"")) + "]";

        return new FakeController(listener, $"127.0.0.1:{port}", document, metadata);
    }

    /// <summary>The usermod block as an <c>info</c> member, or nothing at all when there is none.</summary>
    private static string Heard(string? usermods) =>
        usermods is { Length: > 0 } said ? $", \"u\": {said}" : string.Empty;

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

                lock (_posted)
                {
                    _posted.Add(sent);
                }

                context.Response.ContentType = "application/json";
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
