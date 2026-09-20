using System.Net.Http.Json;
using System.Text.Json;

namespace Ledwright.Core;

/// <summary>One LED output as the controller's hardware configuration describes it.</summary>
/// <param name="Start">First LED index this output drives.</param>
/// <param name="Length">How many LEDs are wired to it.</param>
/// <param name="Pins">GPIO pins in use.</param>
/// <param name="ColorOrder">WLED's colour-order code.</param>
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
/// Reads are safe. Writes are deliberately not wrapped here: a malformed <c>/cfg.json</c> post can
/// leave a controller needing a factory reset, and it generally forces a reboot. Use
/// <see cref="GetRawAsync"/>, change what you need, and post it back yourself once you have a backup.
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
