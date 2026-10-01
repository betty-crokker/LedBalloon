using System.Net;
using System.Net.Http.Headers;

namespace LedBalloon.Core;

/// <summary>
/// Reads and writes files on a WLED device's flash filesystem.
/// <para>
/// This is what makes "no settings on the PC" possible: the controller already stores its own
/// presets and config as files, and there is room beside them for LedBalloon's project. On the two
/// ESP32 controllers this was developed against there was roughly 935 KB free of 983 KB.
/// </para>
/// <para>
/// Reads go straight to the file path, which is how <c>/presets.json</c> and <c>/cfg.json</c> are
/// served. Writes go through the <c>/edit</c> handler that backs WLED's built-in file editor.
/// Writes are not universally available — some minimal builds omit the editor — so always check
/// <see cref="WledCapabilities.CanStoreProjectOnDevice"/> and be ready to fall back to a local file.
/// </para>
/// </summary>
public sealed class WledFileSystemClient : IDisposable
{
    private readonly HttpClient _http;

    public WledFileSystemClient(string host, TimeSpan? timeout = null)
    {
        _http = new HttpClient
        {
            BaseAddress = WledClient.NormalizeHost(host),

            // Flash writes on a microcontroller are slow; do not race them.
            Timeout = timeout ?? TimeSpan.FromSeconds(30),
        };
    }

    /// <summary>True when the device exposes the file editor, and so accepts writes.</summary>
    public async Task<bool> SupportsWritesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using HttpResponseMessage response = await _http
                .GetAsync("edit", HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    /// <summary>Reads a file, or returns null when it is not there.</summary>
    public async Task<byte[]?> DownloadAsync(string path, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _http.GetAsync(Normalize(path), cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new WledHttpException(response.StatusCode, $"Could not read {path} from the device.");
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes a file, replacing any existing one.
    /// <para>
    /// Flash has a finite write budget, so save on explicit user action rather than on every edit.
    /// </para>
    /// </summary>
    public async Task UploadAsync(
        string path,
        byte[] content,
        string contentType = "application/json",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        string normalized = Normalize(path);

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        // The handler takes the destination from the part's filename.
        form.Add(file, "data", "/" + normalized);

        HttpStatusCode status;
        using (HttpResponseMessage response = await _http
                   .PostAsync(Endpoint(normalized), form, cancellationToken)
                   .ConfigureAwait(false))
        {
            status = response.StatusCode;
        }

        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new WledHttpException(
                status,
                $"The controller refused to write /{normalized}. A settings PIN blocks the file editor.");
        }

        // The status code is not evidence either way. WLED's file editor answers 500 to a perfectly
        // successful upload — verified against 0.15.3, where the file read back byte for byte after
        // a 500. So confirm the write by reading it back rather than trusting the response.
        await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);

        if (!await WroteSuccessfullyAsync(normalized, content.Length, cancellationToken).ConfigureAwait(false))
        {
            throw new WledHttpException(
                status,
                $"Could not write /{normalized} to the device (it answered {(int)status} and the file " +
                "did not come back). The build may omit the file editor, or the filesystem may be full.");
        }
    }

    /// <summary>
    /// Where to post a file so the controller does something about it.
    /// </summary>
    /// <remarks>
    /// Two handlers take an upload and they are not interchangeable. <c>/edit</c> writes the file
    /// and stops there; <c>/upload</c> writes it and then tells the firmware to look at it again.
    /// For a custom palette that difference is the whole thing: posted to <c>/edit</c> the file sits
    /// on the filesystem, <c>cpalcount</c> never moves, and the palette does not exist as far as any
    /// segment is concerned - and WLED does not complain, it silently falls back to palette 0.
    /// Which is exactly the failure <see cref="Layout.CustomPaletteCopier"/> was written to prevent,
    /// arriving through the door it uses to prevent it.
    /// <para>
    /// Measured on 0.15.3: the same bytes to <c>/edit</c> left <c>cpalcount</c> at 1 and the palette
    /// absent from <c>/json/palx</c> half a minute later; to <c>/upload</c> it was 2 and the gradient
    /// was there. WLED's own palette editor posts to <c>/upload</c>.
    /// </para>
    /// <para>
    /// Only palettes are routed that way. The rest keep <c>/edit</c>, which is what they have always
    /// used and what the preset and photo paths were tested against.
    /// </para>
    /// </remarks>
    private static string Endpoint(string normalized) =>
        normalized.StartsWith("palette", StringComparison.OrdinalIgnoreCase) ? "upload" : "edit";

    /// <summary>Reads the file back and checks it is the size we just sent.</summary>
    private async Task<bool> WroteSuccessfullyAsync(
        string path,
        int expectedLength,
        CancellationToken cancellationToken)
    {
        try
        {
            byte[]? written = await DownloadAsync(path, cancellationToken).ConfigureAwait(false);
            return written is not null && written.Length == expectedLength;
        }
        catch (Exception ex) when (ex is HttpRequestException or WledHttpException or TaskCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Deletes a file from the device, and checks it is gone.
    /// </summary>
    /// <remarks>
    /// The status code is not evidence, the same as it is not for a write. WLED's file editor
    /// answers 200 to a delete of a path that does not exist and never did - demonstrated by
    /// accident, deleting "C:/Program Files/Git/palette1.json" off a controller because a shell had
    /// helpfully rewritten the leading slash. It answered 200, echoed the path back, and of course
    /// removed nothing. Anything that trusted that answer would have reported a file deleted that
    /// was still sitting there.
    /// </remarks>
    public async Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        string normalized = Normalize(path);

        using var request = new HttpRequestMessage(HttpMethod.Delete, "edit")
        {
            Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("path", "/" + normalized)]),
        };

        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
        {
            throw new WledHttpException(response.StatusCode, $"Could not delete /{normalized} from the device.");
        }

        await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);

        if (await StillThereAsync(normalized, cancellationToken).ConfigureAwait(false))
        {
            throw new WledHttpException(
                response.StatusCode,
                $"Could not delete /{normalized} from the device (it answered " +
                $"{(int)response.StatusCode} and the file is still there). The build may omit the " +
                "file editor, or the path may not be the one the device knows it by.");
        }
    }

    /// <summary>Whether the file still answers after being told to go.</summary>
    private async Task<bool> StillThereAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await DownloadAsync(path, cancellationToken).ConfigureAwait(false) is not null;
        }
        catch (Exception ex) when (ex is HttpRequestException or WledHttpException or TaskCanceledException)
        {
            // Unreachable rather than present. Saying it survived would turn a network blip into a
            // failed delete that had in fact worked.
            return false;
        }
    }

    private static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return path.TrimStart('/');
    }

    public void Dispose() => _http.Dispose();
}
