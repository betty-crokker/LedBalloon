using System.Net;
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
/// </summary>
internal sealed class FakeController : IDisposable
{
    /// <summary>What the controller calls itself, and so what anything keyed by controller uses.</summary>
    public const string Key = "aa:bb:cc:dd:ee:ff";

    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _serving;

    private FakeController(HttpListener listener, string host, string document)
    {
        _listener = listener;
        Host = host;
        _serving = Task.Run(() => ServeAsync(document));
    }

    /// <summary>Where to point a device at, as the app would be given it.</summary>
    public string Host { get; }

    public static FakeController Start(params string[] effects)
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
              "state": { "on": false, "bri": 128, "seg": [ { "id": 0, "start": 0, "stop": 10, "len": 10 } ] },
              "info": { "name": "Fake", "ver": "0.15.3", "mac": "{{Key}}", "leds": { "count": 10, "fps": 0 } },
              "effects": [ {{names}} ],
              "palettes": [ "Default", "Random Cycle" ]
            }
            """;

        return new FakeController(listener, $"127.0.0.1:{port}", document);
    }

    private static int FreePort()
    {
        using var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);

        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        return port;
    }

    private async Task ServeAsync(string document)
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
