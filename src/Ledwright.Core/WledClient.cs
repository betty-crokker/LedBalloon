using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Ledwright.Core.Json;
using Ledwright.Core.Models;

namespace Ledwright.Core;

/// <summary>
/// The HTTP half of the WLED interface: one-shot reads and state patches over <c>/json</c>.
/// <para>
/// Keep one instance per device for the lifetime of the app. The pooled connection settings below
/// exist so that a user nudging a slider does not pay a fresh TCP handshake to a microcontroller
/// on every interaction.
/// </para>
/// </summary>
public sealed class WledClient : IDisposable
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;

    /// <param name="host">An IP, a hostname, or a full URL. "192.168.1.50" and "wled-a1b2c3.local" both work.</param>
    public WledClient(string host, TimeSpan? timeout = null)
        : this(CreateHttpClient(NormalizeHost(host), timeout), ownsHttpClient: true)
    {
    }

    public WledClient(HttpClient httpClient, bool ownsHttpClient = false)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        if (httpClient.BaseAddress is null)
        {
            throw new ArgumentException("The HttpClient needs a BaseAddress pointing at the device.", nameof(httpClient));
        }

        _http = httpClient;
        _ownsHttpClient = ownsHttpClient;
    }

    public Uri BaseAddress => _http.BaseAddress!;

    public string Host => BaseAddress.Host;

    /// <summary>Accepts "1.2.3.4", "wled-x.local", "wled-x.local:80" or "http://1.2.3.4/" and returns a clean base URL.</summary>
    public static Uri NormalizeHost(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        string trimmed = host.Trim();
        if (!trimmed.Contains("://", StringComparison.Ordinal))
        {
            // WLED speaks plain HTTP only; there is no TLS listener to fall back to.
            trimmed = "http://" + trimmed;
        }

        var parsed = new Uri(trimmed, UriKind.Absolute);
        return new Uri(parsed.GetLeftPart(UriPartial.Authority) + "/");
    }

    private static HttpClient CreateHttpClient(Uri baseAddress, TimeSpan? timeout)
    {
        var handler = new SocketsHttpHandler
        {
            // Hold the connection open between user interactions rather than reconnecting each time.
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
            PooledConnectionLifetime = TimeSpan.FromMinutes(30),

            // An ESP8266 has a very small connection table. Do not stampede it.
            MaxConnectionsPerServer = 2,
            ConnectTimeout = TimeSpan.FromSeconds(3),
        };

        return new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = baseAddress,
            Timeout = timeout ?? DefaultTimeout,
        };
    }

    /// <summary>State, info, effect names and palette names in a single round trip.</summary>
    public Task<WledResponse?> GetAsync(CancellationToken cancellationToken = default) =>
        GetJsonAsync("json", WledJson.Default.WledResponse, cancellationToken);

    public Task<WledState?> GetStateAsync(CancellationToken cancellationToken = default) =>
        GetJsonAsync("json/state", WledJson.Default.WledState, cancellationToken);

    public Task<WledInfo?> GetInfoAsync(CancellationToken cancellationToken = default) =>
        GetJsonAsync("json/info", WledJson.Default.WledInfo, cancellationToken);

    /// <summary>
    /// Effect names in <c>fx</c> index order. Always read these from the device: the list differs
    /// between firmware versions and between stock and sound-reactive builds.
    /// </summary>
    public Task<string[]?> GetEffectsAsync(CancellationToken cancellationToken = default) =>
        GetJsonAsync("json/eff", WledJson.Default.StringArray, cancellationToken);

    /// <summary>Palette names in <c>pal</c> index order.</summary>
    public Task<string[]?> GetPalettesAsync(CancellationToken cancellationToken = default) =>
        GetJsonAsync("json/pal", WledJson.Default.StringArray, cancellationToken);

    /// <summary>
    /// Reads every preset stored on the device, keyed by slot number and sorted by it.
    /// <para>
    /// Presets are deliberately absent from <c>/json</c>; they live in a file on the device's flash,
    /// which is why this reads <c>/presets.json</c> instead. Anything a vendor preloaded (Gledopto
    /// units ship with a set) is an ordinary WLED preset and comes back here with the rest.
    /// </para>
    /// <para>
    /// This file can run to tens of kilobytes and is read off flash, so it is noticeably slower than
    /// a <c>/json</c> call. Fetch it once at connect and cache it; nothing changes it but the user.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<WledPreset>> GetPresetsAsync(CancellationToken cancellationToken = default)
    {
        Dictionary<string, WledPreset>? raw = await GetJsonAsync(
            "presets.json",
            WledJson.Default.DictionaryStringWledPreset,
            cancellationToken).ConfigureAwait(false);

        if (raw is null)
        {
            return [];
        }

        var presets = new List<WledPreset>(raw.Count);
        foreach ((string key, WledPreset preset) in raw)
        {
            // Slot 0 is WLED's scratch slot for the current unsaved look; it is never a real preset.
            if (!int.TryParse(key, out int id) || id == 0 || preset is null || preset.IsEmpty)
            {
                continue;
            }

            preset.Id = id;
            presets.Add(preset);
        }

        presets.Sort(static (a, b) => a.Id.CompareTo(b.Id));
        return presets;
    }

    /// <summary>Applies a stored preset by slot number.</summary>
    public Task<WledState?> ApplyPresetAsync(int presetId, CancellationToken cancellationToken = default) =>
        ApplyAsync(new WledState { Preset = presetId }, cancellationToken);

    /// <summary>
    /// Sends a partial state update. Anything left null on <paramref name="patch"/> is untouched on the device.
    /// Returns the resulting state when the patch set <see cref="WledState.ReturnFullState"/>, else null.
    /// </summary>
    public async Task<WledState?> ApplyAsync(WledState patch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patch);

        // Serialized up front and sent as a byte array on purpose. PostAsJsonAsync streams the
        // content, which leaves HttpClient unable to compute a Content-Length, so it falls back to
        // Transfer-Encoding: chunked — and WLED's embedded server answers 400 to a chunked request
        // body. Verified against 0.15.3: the identical JSON with an explicit length is accepted.
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(patch, WledJson.Default.WledState);

        using var content = new ByteArrayContent(payload);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using HttpResponseMessage response = await _http
            .PostAsync("json/state", content, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        // Without "v":true the device just acknowledges with {"success":true}.
        if (string.IsNullOrWhiteSpace(body) || body.Contains("\"success\"", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(body, WledJson.Default.WledState);
        }
        catch (JsonException ex)
        {
            throw new WledException($"Could not read the state {Host} returned: {Truncate(body)}", ex);
        }
    }

    /// <summary>
    /// Applies a patch and reads back the full resulting state in one round trip.
    /// Note this sets <see cref="WledState.ReturnFullState"/> on the patch you pass in.
    /// </summary>
    public Task<WledState?> ApplyAndReadAsync(WledState patch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patch);

        patch.ReturnFullState = true;
        return ApplyAsync(patch, cancellationToken);
    }

    /// <summary>Cheap liveness probe. Returns the device info, or null when it does not answer in time.</summary>
    public async Task<WledInfo?> PingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await GetInfoAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or WledException)
        {
            return null;
        }
    }

    private async Task<T?> GetJsonAsync<T>(string path, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _http.GetAsync(path, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        try
        {
            return await response.Content.ReadFromJsonAsync(typeInfo, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new WledException($"{Host} returned something that is not WLED JSON at /{path}.", ex);
        }
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw new WledHttpException(
            response.StatusCode,
            $"{Host} answered {(int)response.StatusCode} {response.StatusCode}: {Truncate(body)}");
    }

    private static string Truncate(string value) =>
        value.Length <= 200 ? value : value[..200] + "...";

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }
}
