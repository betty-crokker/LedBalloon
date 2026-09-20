using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ledwright.Core;

/// <summary>One LED output as the controller's hardware configuration describes it.</summary>
/// <param name="Start">First LED index this output drives.</param>
/// <param name="Length">How many LEDs are wired to it.</param>
/// <param name="Pins">GPIO pins in use.</param>
/// <param name="ColorOrder">WLED's color-order code.</param>
/// <param name="Reversed">Whether the output is configured to run backwards.</param>
public sealed record LedBus(int Start, int Length, int[] Pins, int ColorOrder, bool Reversed)
{
    public int StopExclusive => Start + Length;

    public override string ToString() => $"[{Start}..{StopExclusive}) x{Length} on pin {string.Join(",", Pins)}";
}

/// <summary>
/// Reads the controller's hardware configuration from <c>/cfg.json</c>.
/// <para>
/// This is the other half of the WLED interface, and the answer to "is everything on the web page
/// available as JSON?". The main control page maps entirely onto <c>/json/state</c>. The settings
/// pages behind the gear icon do not: they are HTML forms that post to <c>/settings/*</c>. But the
/// same configuration is readable, whole, as <c>/cfg.json</c> — which is what WLED's own
/// backup-and-restore uses — including the per-output LED counts that decide how long your segments
/// really are.
/// </para>
/// <para>
/// Reading it is safe. Writing it back is not wrapped here, for two reasons: a malformed post can
/// leave a controller needing a factory reset, and on 0.15.3 posting a modified configuration
/// document back does not work at all — it answers 200 and changes nothing, verified by reading it
/// back afterwards. Settings that need changing go through the same form endpoints WLED's own
/// settings pages use; see <see cref="SetDeviceNameAsync"/>.
/// </para>
/// </summary>
public sealed class WledConfigClient
{
    private readonly HttpClient _http;

    public WledConfigClient(string host, TimeSpan? timeout = null)
    {
        _http = new HttpClient
        {
            BaseAddress = WledClient.NormalizeHost(host),
            Timeout = timeout ?? TimeSpan.FromSeconds(10),
        };
    }

    /// <summary>The whole configuration document, for inspection or backup.</summary>
    public async Task<JsonDocument> GetRawAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _http.GetAsync("cfg.json", cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new WledHttpException(
                response.StatusCode,
                $"Could not read /cfg.json ({(int)response.StatusCode}). A settings PIN will block this.");
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The configured LED outputs, read from <c>hw.led.ins</c>. Parsed defensively: the shape has
    /// shifted across firmware versions, and an unreadable entry is skipped rather than fatal.
    /// </summary>
    public async Task<IReadOnlyList<LedBus>> GetLedBusesAsync(CancellationToken cancellationToken = default)
    {
        using JsonDocument config = await GetRawAsync(cancellationToken).ConfigureAwait(false);

        if (!TryGetLedSection(config.RootElement, out JsonElement led) ||
            !led.TryGetProperty("ins", out JsonElement instances) ||
            instances.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var buses = new List<LedBus>();
        foreach (JsonElement instance in instances.EnumerateArray())
        {
            if (instance.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            buses.Add(new LedBus(
                Start: ReadInt(instance, "start"),
                Length: ReadInt(instance, "len"),
                Pins: ReadIntArray(instance, "pin"),
                ColorOrder: ReadInt(instance, "order"),
                Reversed: ReadBool(instance, "rev")));
        }

        return buses;
    }

    /// <summary>
    /// Total LEDs the controller is configured to drive. Compare against
    /// <see cref="Layout.LedwrightProject.TotalLeds"/> to catch a layout that has drifted from the hardware.
    /// </summary>
    public async Task<int> GetTotalLedCountAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<LedBus> buses = await GetLedBusesAsync(cancellationToken).ConfigureAwait(false);
        return buses.Count == 0 ? 0 : buses.Max(b => b.StopExclusive);
    }

    /// <summary>
    /// Renames the device on the controller itself, so the name follows it into the WLED app, the
    /// web UI and anything else that talks to it.
    /// <para>
    /// This goes through the settings form at <c>/settings/ui</c> rather than <c>/cfg.json</c>.
    /// Posting a modified configuration document back is the obvious approach and it does not work:
    /// on 0.15.3 it answers 200 and changes nothing, which is worse than failing. The form endpoint
    /// is what WLED's own settings page uses and it does take effect.
    /// </para>
    /// <para>
    /// The form carries the whole page, so a field left out is a field cleared. The simplified-UI
    /// checkbox that shares this page is read first and sent back untouched.
    /// </para>
    /// </summary>
    public async Task SetDeviceNameAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        bool simplifiedUi = await ReadSimplifiedUiAsync(cancellationToken).ConfigureAwait(false);

        var fields = new List<KeyValuePair<string, string>>
        {
            new("DS", name.Trim()),
        };

        if (simplifiedUi)
        {
            fields.Add(new KeyValuePair<string, string>("SU", "on"));
        }

        using var form = new FormUrlEncodedContent(fields);
        using HttpResponseMessage write = await _http.PostAsync("settings/ui", form, cancellationToken)
            .ConfigureAwait(false);

        if (!write.IsSuccessStatusCode)
        {
            throw new WledHttpException(
                write.StatusCode,
                $"The controller rejected the rename ({(int)write.StatusCode}). A settings PIN will block this.");
        }

        // The status code is not proof, as /cfg.json demonstrated. Read the name back.
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);

        string? applied = await ReadDeviceNameAsync(cancellationToken).ConfigureAwait(false);
        if (!string.Equals(applied, name.Trim(), StringComparison.Ordinal))
        {
            throw new WledException(
                $"The controller still calls itself '{applied}'. The rename did not take.");
        }
    }

    /// <summary>The name the device reports for itself.</summary>
    public async Task<string?> ReadDeviceNameAsync(CancellationToken cancellationToken = default)
    {
        using JsonDocument config = await GetRawAsync(cancellationToken).ConfigureAwait(false);

        return config.RootElement.TryGetProperty("id", out JsonElement id) &&
               id.TryGetProperty("name", out JsonElement name)
            ? name.GetString()
            : null;
    }

    private async Task<bool> ReadSimplifiedUiAsync(CancellationToken cancellationToken)
    {
        using JsonDocument config = await GetRawAsync(cancellationToken).ConfigureAwait(false);

        return config.RootElement.TryGetProperty("id", out JsonElement id) &&
               id.TryGetProperty("sui", out JsonElement sui) &&
               sui.ValueKind == JsonValueKind.True;
    }

    private static bool TryGetLedSection(JsonElement root, out JsonElement led)
    {
        led = default;
        return root.TryGetProperty("hw", out JsonElement hardware) &&
               hardware.TryGetProperty("led", out led);
    }

    private static int ReadInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int result) ? result : 0;

    private static bool ReadBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;

    private static int[] ReadIntArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var items = new List<int>();
        foreach (JsonElement item in value.EnumerateArray())
        {
            if (item.TryGetInt32(out int parsed))
            {
                items.Add(parsed);
            }
        }

        return [.. items];
    }
}
