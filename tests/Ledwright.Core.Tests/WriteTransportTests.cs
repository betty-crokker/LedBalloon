using System.Net;
using Ledwright.Core;
using Ledwright.Core.Models;
using Xunit;

namespace Ledwright.Core.Tests;

/// <summary>
/// How a state patch goes out on the wire.
/// <para>
/// This is here because of a bug that cost an evening: every write through the HTTP client came
/// back 400 from a real controller while the identical JSON sent by hand was accepted. The content
/// was being streamed, so HttpClient could not compute a length and fell back to chunked transfer
/// encoding, which WLED's embedded server rejects.
/// </para>
/// </summary>
public class WriteTransportTests
{
    [Fact]
    public async Task A_state_patch_is_sent_with_a_content_length()
    {
        var handler = new CapturingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://controller/") };
        using var client = new WledClient(http);

        await client.ApplyAsync(new WledState { On = false });

        // A null length here means chunked, and chunked means every write silently fails.
        Assert.NotNull(handler.ContentLength);
        Assert.Equal("""{"on":false}""", handler.Body);
    }

    [Fact]
    public async Task A_state_patch_is_sent_as_plain_json()
    {
        var handler = new CapturingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://controller/") };
        using var client = new WledClient(http);

        await client.ApplyAsync(new WledState { Brightness = 128 });

        Assert.Equal("application/json", handler.ContentType);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/json/state", handler.Path);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public long? ContentLength { get; private set; }

        public string? ContentType { get; private set; }

        public string? Body { get; private set; }

        public HttpMethod? Method { get; private set; }

        public string? Path { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            Path = request.RequestUri?.AbsolutePath;

            if (request.Content is { } content)
            {
                // Read the length before the body, which is what a real client does when deciding
                // whether it can avoid chunking.
                ContentLength = content.Headers.ContentLength;
                ContentType = content.Headers.ContentType?.MediaType;
                Body = await content.ReadAsStringAsync(cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"success":true}"""),
            };
        }
    }
}
