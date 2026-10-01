using System.Net;
using System.Text;
using LedBalloon.Core;
using LedBalloon.Core.Layout;
using Xunit;

namespace LedBalloon.Core.Tests;

/// <summary>
/// A palette made on one controller, wanted on the other.
/// <para>
/// Which box a gradient's file sits on is not something anybody making a house look nice should
/// have to think about, and WLED gives no help at all: a segment asking for a custom palette the
/// controller does not hold falls silently back to palette 0, so the run comes out plain instead of
/// red, white and blue with nothing said about it.
/// </para>
/// <para>
/// The numbering makes it worse. Custom palettes are positions in each controller's own file list,
/// counted down from 255, so the same gradient is a different id on each box and copying it almost
/// never leaves the id alone.
/// </para>
/// </summary>
public class PuttingApaletteWhereItIsWantedTests
{
    private sealed class Controller : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Task _serving;

        public Controller(params string[] held)
        {
            int port = FreePort();
            Host = $"127.0.0.1:{port}";

            for (int slot = 0; slot < held.Length; slot++)
            {
                Files[$"palette{slot}.json"] = Encoding.UTF8.GetBytes(held[slot]);
            }

            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();

            _serving = Task.Run(ServeAsync);
        }

        public string Host { get; }

        public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);

        /// <summary>Where each upload was posted, so the endpoint can be checked as well.</summary>
        public List<string> Posted { get; } = [];

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
                    // The part's filename says where it goes; the body is read to the end so the
                    // client sees a completed request.
                    using var body = new MemoryStream();
                    await context.Request.InputStream.CopyToAsync(body);

                    byte[] raw = body.ToArray();
                    string text = Encoding.UTF8.GetString(raw);

                    int at = text.IndexOf("filename=\"/", StringComparison.Ordinal);
                    if (at >= 0)
                    {
                        int from = at + 11;
                        string name = text[from..text.IndexOf('"', from)];

                        // The payload sits between the blank line after the headers and the closing
                        // boundary; good enough for a stand-in.
                        int start = text.IndexOf("\r\n\r\n", at, StringComparison.Ordinal) + 4;
                        int end = text.LastIndexOf("\r\n--", StringComparison.Ordinal);

                        lock (Posted)
                        {
                            Posted.Add(path);
                            Files[name] = Encoding.UTF8.GetBytes(text[start..end]);
                        }
                    }
                }
                else
                {
                    byte[]? held;

                    lock (Posted)
                    {
                        Files.TryGetValue(path, out held);
                    }

                    if (held is null)
                    {
                        context.Response.StatusCode = 404;
                    }
                    else
                    {
                        await context.Response.OutputStream.WriteAsync(held);
                    }
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

    private const string RedWhiteBlue = """{"palette":[0,"ff0000",128,"ffffff",255,"0000ff"]}""";
    private const string Icicles = """{"palette":[0,"002b55",255,"ffffff"]}""";

    [Fact]
    public async Task A_controller_that_has_none_takes_it_as_its_first()
    {
        using var south = new Controller();

        int id = await CustomPaletteCopier.EnsureAsync(
            south.Host, Encoding.UTF8.GetBytes(RedWhiteBlue));

        // palette0 is the first file WLED reads, and it answers to 255.
        Assert.Equal(255, id);
        Assert.Equal(RedWhiteBlue, Encoding.UTF8.GetString(south.Files["palette0.json"]));
    }

    [Fact]
    public async Task One_that_already_has_it_is_not_given_it_twice()
    {
        using var south = new Controller(RedWhiteBlue);

        int id = await CustomPaletteCopier.EnsureAsync(
            south.Host, Encoding.UTF8.GetBytes(RedWhiteBlue));

        Assert.Equal(255, id);
        Assert.Empty(south.Posted);
    }

    [Fact]
    public async Task And_one_that_has_another_takes_it_as_its_second()
    {
        using var south = new Controller(Icicles);

        int id = await CustomPaletteCopier.EnsureAsync(
            south.Host, Encoding.UTF8.GetBytes(RedWhiteBlue));

        // The id it had on the other controller is no guide at all: this is position two here, so
        // it is 254, whatever it answered to where it came from.
        Assert.Equal(254, id);
        Assert.Equal(Icicles, Encoding.UTF8.GetString(south.Files["palette0.json"]));
        Assert.Equal(RedWhiteBlue, Encoding.UTF8.GetString(south.Files["palette1.json"]));
    }

    [Fact]
    public async Task It_goes_to_the_handler_that_makes_the_controller_notice()
    {
        using var south = new Controller();

        await CustomPaletteCopier.EnsureAsync(south.Host, Encoding.UTF8.GetBytes(RedWhiteBlue));

        // /edit would write the file and leave the firmware none the wiser.
        Assert.Contains("upload", south.Posted);
    }

    [Fact]
    public async Task A_controller_with_no_room_says_so_rather_than_losing_one()
    {
        string[] full = [.. Enumerable.Range(0, CustomPaletteCopier.MaxSlots)
            .Select(i => $$"""{"palette":[0,"0000{{i:x2}}",255,"ffffff"]}""")];

        using var south = new Controller(full);

        WledException ex = await Assert.ThrowsAsync<WledException>(
            () => CustomPaletteCopier.EnsureAsync(south.Host, Encoding.UTF8.GetBytes(RedWhiteBlue)));

        Assert.Contains("all 10 custom palettes", ex.Message);
        Assert.Empty(south.Posted);
    }
}
