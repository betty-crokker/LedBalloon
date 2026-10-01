using System.Net;
using LedBalloon.Core;
using Xunit;

namespace LedBalloon.Core.Tests;

/// <summary>
/// A custom palette has to be posted to the handler that tells the firmware to read it again.
/// <para>
/// WLED has two upload handlers and they are not interchangeable. <c>/edit</c> writes the file and
/// stops; <c>/upload</c> writes it and reloads the custom palettes. Posted to <c>/edit</c>, a
/// palette file sits on the filesystem, <c>cpalcount</c> never moves, and no segment can see it —
/// and WLED does not complain, it quietly falls back to palette 0.
/// </para>
/// <para>
/// Which is the exact failure <see cref="LedBalloon.Core.Layout.CustomPaletteCopier"/> exists to
/// prevent, arriving through the door it uses to prevent it: every palette the app has ever carried
/// to another controller landed and was never loaded.
/// </para>
/// <para>
/// Measured on 0.15.3 before it was fixed. The same bytes to <c>/edit</c> left <c>cpalcount</c> at 1
/// and the palette absent from <c>/json/palx</c> thirty seconds later; to <c>/upload</c> it read 2
/// and the gradient was there. WLED's own palette editor posts to <c>/upload</c>.
/// </para>
/// </summary>
public class ApaletteHasToBeUploadedWhereWledLooksTests
{
    private sealed class Listener : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Task _serving;

        public Listener()
        {
            int port = FreePort();
            Host = $"127.0.0.1:{port}";

            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();

            _serving = Task.Run(ServeAsync);
        }

        public string Host { get; }

        /// <summary>Every path posted to, in order.</summary>
        public List<string> Posted { get; } = [];

        /// <summary>What a download returns, so the upload's read-back check can be satisfied.</summary>
        public byte[] Stored { get; set; } = [];

        private static int FreePort()
        {
            using var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);

            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            return port;
        }

        private async Task ServeAsync()
        {
            while (_listener.IsListening)
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

                string path = (context.Request.Url?.AbsolutePath ?? "/").TrimStart('/');

                if (context.Request.HttpMethod == "POST")
                {
                    lock (Posted)
                    {
                        Posted.Add(path);
                    }
                }
                else if (Stored.Length > 0)
                {
                    await context.Response.OutputStream.WriteAsync(Stored);
                }

                context.Response.Close();
            }
        }

        public void Dispose()
        {
            _listener.Stop();
            _listener.Close();

            try
            {
                _serving.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
                // Shutting down.
            }
        }
    }

    [Fact]
    public async Task A_palette_goes_to_the_handler_that_reloads_them()
    {
        using var controller = new Listener();
        using var client = new WledFileSystemClient(controller.Host);

        byte[] content = """{"palette":[0,"ff0000",255,"0000ff"]}"""u8.ToArray();
        controller.Stored = content;

        await client.UploadAsync("palette1.json", content);

        Assert.Contains("upload", controller.Posted);
        Assert.DoesNotContain("edit", controller.Posted);
    }

    [Fact]
    public async Task Everything_else_keeps_the_file_editor()
    {
        // Only palettes are routed the other way. Presets and the photo have always gone through the
        // editor and were tested against it; there is no reason to move them and a reason not to.
        using var controller = new Listener();
        using var client = new WledFileSystemClient(controller.Host);

        byte[] content = """{"0":{}}"""u8.ToArray();
        controller.Stored = content;

        await client.UploadAsync("presets.json", content);

        Assert.Contains("edit", controller.Posted);
        Assert.DoesNotContain("upload", controller.Posted);
    }
}
