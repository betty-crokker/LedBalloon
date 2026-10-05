using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LedBalloon.Core;

/// <summary>One LED output as the controller's hardware configuration describes it.</summary>
/// <param name="Start">First LED index this output drives.</param>
/// <param name="Length">How many LEDs are wired to it.</param>
/// <param name="Pins">GPIO pins in use.</param>
/// <param name="ColorOrder">WLED's color-order code.</param>
/// <param name="Reversed">Whether the output is configured to run backwards.</param>
/// <param name="MilliampsPerLed">Current budgeted per LED, for the power limiter.</param>
/// <param name="SkipFirst">LEDs at the head of the output that are wired but not used.</param>
/// <param name="OffRefresh">Whether to keep refreshing the output while it is off.</param>
/// <param name="Type">WLED's LED type code. 22 is the common WS281x.</param>
public sealed record LedBus(
    int Start,
    int Length,
    int[] Pins,
    int ColorOrder,
    bool Reversed,
    int MilliampsPerLed = 0,
    int SkipFirst = 0,
    bool OffRefresh = false,
    int Type = 22)
{
    public int StopExclusive => Start + Length;

    public override string ToString() => $"[{Start}..{StopExclusive}) x{Length} on pin {string.Join(",", Pins)}";
}

/// <summary>
/// Which LED outputs a stretch of the wire lands on.
/// <para>
/// A run does not choose an output: it is plugged into one, and where it sits follows. Kept here
/// so that anything wanting to say "output 2, GPIO 2" says it the same way.
/// </para>
/// </summary>
public static class LedOutputMap
{
    /// <summary>The outputs this range touches, paired with their 1-based numbers.</summary>
    public static IReadOnlyList<(int Number, LedBus Bus)> Covering(
        IReadOnlyList<LedBus> outputs, int start, int stopExclusive)
    {
        ArgumentNullException.ThrowIfNull(outputs);

        var touched = new List<(int, LedBus)>();

        for (int i = 0; i < outputs.Count; i++)
        {
            if (start < outputs[i].StopExclusive && stopExclusive > outputs[i].Start)
            {
                touched.Add((i + 1, outputs[i]));
            }
        }

        return touched;
    }
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
/// Reading it is safe. Writing needs care, and the rule is: post the whole document back, never
/// part of it.
/// </para>
/// <para>
/// A full round trip through <c>POST /json/cfg</c> is faithful — read <c>cfg.json</c>, post it
/// back untouched, read it again and it is identical byte for byte, measured on 0.15.3. A partial
/// post is not. Most of the handler assigns only the keys it finds, but a few lines read straight
/// through a missing section, and <c>strip.setTargetFps(hw_led["fps"])</c> is one: a document
/// without that key sets the target frame rate to zero, which uncaps it. A controller configured
/// for 42 frames a second was left rendering flat out at 96 by a post that mentioned only timers.
/// </para>
/// <para>
/// The device name is the exception that still needs a form. Posting it in the configuration
/// answers 200 and changes nothing, so <see cref="SetDeviceNameAsync"/> goes through the same
/// <c>/settings/ui</c> endpoint WLED's own settings page uses.
/// </para>
/// </summary>
public sealed class WledConfigClient
{
    /// <summary>How many times a configuration read is attempted before giving up on it.</summary>
    private const int Attempts = 3;

    /// <summary>How long to leave a busy controller alone, multiplied by the attempt.</summary>
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(400);

    private readonly HttpClient _http;

    private readonly string _host;

    public WledConfigClient(string host, TimeSpan? timeout = null)
    {
        _host = host;

        _http = new HttpClient
        {
            BaseAddress = WledClient.NormalizeHost(host),
            Timeout = timeout ?? TimeSpan.FromSeconds(10),
        };
    }

    /// <summary>The whole configuration document, for inspection or backup.</summary>
    public async Task<JsonDocument> GetRawAsync(CancellationToken cancellationToken = default) =>
        JsonDocument.Parse(await ReadConfigurationTextAsync(cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// The configuration as it came off the wire, read again if it comes back empty.
    /// </summary>
    /// <remarks>
    /// An ESP32 part-way through a flash write answers with 200 and no body at all, and saving is
    /// several flash writes in a row - the presets, the project file - so the read that follows one
    /// lands in exactly that window. What came out was System.Text.Json's own words, "The input does
    /// not contain any JSON tokens", attached to a save that had in fact stored everything: a
    /// frightening sentence about a device that was simply busy for a moment.
    /// <para>
    /// Here rather than in each caller, because every one of them wants the same thing and none of
    /// them wants to know this about ESP32s.
    /// </para>
    /// </remarks>
    private async Task<string> ReadConfigurationTextAsync(CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            using HttpResponseMessage response = await _http.GetAsync("cfg.json", cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new WledHttpException(
                    response.StatusCode,
                    $"Could not read /cfg.json ({(int)response.StatusCode}). A settings PIN will block this.");
            }

            string body = await response.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            // Anything that parses is taken, including something that is not an object: that is a
            // real answer and a different complaint, made by whoever asked for it.
            if (!string.IsNullOrWhiteSpace(body) && Parses(body))
            {
                return body;
            }

            if (attempt >= Attempts)
            {
                throw new WledException(
                    $"{_host} would not give up its settings - it answered {Attempts} times with " +
                    "nothing usable. A controller busy writing its flash does this; try again in a " +
                    "moment.");
            }

            await Task.Delay(SettleDelay * attempt, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Whether this is JSON at all. A half-written body reads as malformed, not as empty.</summary>
    private static bool Parses(string body)
    {
        try
        {
            return JsonNode.Parse(body) is not null;
        }
        catch (JsonException)
        {
            return false;
        }
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
                Reversed: ReadBool(instance, "rev"),
                MilliampsPerLed: ReadInt(instance, "ledma"),
                SkipFirst: ReadInt(instance, "skip"),
                OffRefresh: ReadBool(instance, "ref"),
                Type: ReadInt(instance, "type")));
        }

        return buses;
    }

    /// <summary>
    /// Total LEDs the controller is configured to drive. Compare against
    /// <see cref="Layout.LedBalloonProject.TotalLeds"/> to catch a layout that has drifted from the hardware.
    /// </summary>
    public async Task<int> GetTotalLedCountAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<LedBus> buses = await GetLedBusesAsync(cancellationToken).ConfigureAwait(false);
        return buses.Count == 0 ? 0 : buses.Max(b => b.StopExclusive);
    }

    /// <summary>
    /// How often the controller aims to redraw, from <c>hw.led.fps</c>.
    /// <para>
    /// Read from the configuration rather than from <c>/json/info</c> on purpose. Info reports the
    /// rate actually being achieved, which is the better number but is zero whenever the lights are
    /// off - which is exactly when a preset is being looked at rather than used. The configured
    /// target is always there, and is an upper bound on the other.
    /// </para>
    /// <para>
    /// It matters because an effect that fades or trails does so once per frame, not once per
    /// millisecond, so how long a trail looks depends on this number.
    /// </para>
    /// </summary>
    public async Task<int?> GetTargetFpsAsync(CancellationToken cancellationToken = default)
    {
        using JsonDocument document = await GetRawAsync(cancellationToken).ConfigureAwait(false);

        if (!TryGetLedSection(document.RootElement, out JsonElement led))
        {
            return null;
        }

        int fps = ReadInt(led, "fps");
        return fps is > 0 and <= 255 ? fps : null;
    }

    /// <summary>The timetable this controller keeps for itself.</summary>
    public async Task<IReadOnlyList<ScheduledChange>> GetScheduleAsync(
        CancellationToken cancellationToken = default)
    {
        using JsonDocument document = await GetRawAsync(cancellationToken).ConfigureAwait(false);
        return WledSchedule.Parse(document.RootElement);
    }

    /// <summary>
    /// Replaces the controller's timetable, leaving the rest of its configuration alone.
    /// <para>
    /// The whole configuration is read and posted back with only the timers changed, rather than
    /// posting the timers on their own. A partial post is not safe on 0.15.3: most of the handler
    /// only assigns keys that are present, but a few lines do not, and
    /// <c>strip.setTargetFps(hw_led["fps"])</c> is one of them. A document without that key reads
    /// it as zero and uncaps the frame rate - measured on the development hardware, which went
    /// from a configured 42 frames a second to rendering flat out at 96, and stayed that way.
    /// </para>
    /// <para>
    /// Sun-based entries go last, sunrise before sunset. Their position in the list is the only
    /// thing that records which end of the day they belong to.
    /// </para>
    /// </summary>
    /// <returns>
    /// True when the controller was written to, false when it already held exactly this.
    /// </returns>
    /// <remarks>
    /// The comparison is not an optimization. Flash has a finite write budget, this posts the whole
    /// configuration to change the timers inside it, and a save that changed a colour has no
    /// business rewriting the timetable - saving anything rewrote everything, which is both a cost
    /// and a lie: the progress window named a step that had nothing to do.
    /// </remarks>
    /// <summary>The configuration as an object, waited out the same way.</summary>
    private async Task<JsonObject> ReadConfigurationAsync(CancellationToken cancellationToken) =>
        JsonNode.Parse(await ReadConfigurationTextAsync(cancellationToken).ConfigureAwait(false))
            as JsonObject
        ?? throw new WledException($"{_host} returned a configuration that is not an object.");

    public async Task<bool> SetScheduleAsync(
        IReadOnlyList<ScheduledChange> schedule,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        JsonObject configuration = await ReadConfigurationAsync(cancellationToken)
            .ConfigureAwait(false);

        if (configuration["timers"] is not JsonObject timers)
        {
            timers = [];
            configuration["timers"] = timers;
        }

        var entries = new JsonArray();

        List<ScheduledChange> clock =
            [.. schedule.Where(entry => entry.Sun == SunTrigger.None)
                .OrderBy(entry => entry.Hour)
                .ThenBy(entry => entry.Minute)
                .Take(ClockSlots)];

        // Every clock slot, including the empty ones. The controller keeps eight and fills them by
        // position, so writing only the ones in use leaves whatever was in the rest still there -
        // and a timetable that loses an entry quietly keeps running the entry it lost. Watched it
        // happen: shortening the list left a duplicate behind, and shortening it again left three.
        for (int slot = 0; slot < ClockSlots; slot++)
        {
            entries.Add(slot < clock.Count ? Write(clock[slot]) : Empty());
        }

        // Then the two sun slots, in the order that records which is which.
        entries.Add(WriteSun(schedule, SunTrigger.Sunrise));
        entries.Add(WriteSun(schedule, SunTrigger.Sunset));

        if (timers["ins"] is JsonNode held && held.ToJsonString() == entries.ToJsonString())
        {
            return false;
        }

        timers["ins"] = entries;

        using var content = new StringContent(
            configuration.ToJsonString(), System.Text.Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await _http
            .PostAsync("json/cfg", content, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new WledHttpException(
                response.StatusCode,
                $"That controller would not take the timetable ({(int)response.StatusCode}). " +
                "A settings PIN will block this.");
        }

        return true;
    }

    /// <summary>
    /// Where the controller thinks it is, and which clock it keeps.
    /// </summary>
    /// <returns>
    /// The location, the time zone, and whether it is syncing its clock at all. A controller that
    /// is not syncing has no idea what time it is, so its sun timers are not late - they are
    /// arbitrary.
    /// </returns>
    public async Task<(HouseLocation? Where, int? TimeZone, bool Syncing)> GetLocationAsync(
        CancellationToken cancellationToken = default)
    {
        using JsonDocument config = await GetRawAsync(cancellationToken).ConfigureAwait(false);

        if (!config.RootElement.TryGetProperty("if", out JsonElement network) ||
            !network.TryGetProperty("ntp", out JsonElement ntp))
        {
            return (null, null, false);
        }

        HouseLocation? where =
            ntp.TryGetProperty("lt", out JsonElement lt) && lt.TryGetDouble(out double latitude) &&
            ntp.TryGetProperty("ln", out JsonElement ln) && ln.TryGetDouble(out double longitude)
                ? new HouseLocation(latitude, longitude)
                : null;

        int? zone = ntp.TryGetProperty("tz", out JsonElement tz) && tz.TryGetInt32(out int id)
            ? id
            : null;

        bool syncing = ntp.TryGetProperty("en", out JsonElement en) &&
                       en.ValueKind is JsonValueKind.True;

        return (where, zone, syncing);
    }

    /// <summary>
    /// Tells the controller where it is and which clock to keep, and turns its clock sync on.
    /// <para>
    /// The clock as well as the place, because sunrise is computed from the one and applied in the
    /// other: the right latitude in the wrong zone puts the lights on an hour out, every day,
    /// which looks like a broken timer rather than a wrong setting.
    /// </para>
    /// <para>
    /// Written the same way as the timetable - the whole configuration read and posted back with
    /// only these keys changed - because a partial post is not safe on 0.15.3. See
    /// <see cref="SetScheduleAsync"/>, where leaving out hw.led.fps uncapped the frame rate.
    /// </para>
    /// </summary>
    public async Task SetLocationAsync(
        HouseLocation where,
        int timeZone,
        CancellationToken cancellationToken = default)
    {
        if (!where.IsOnEarth)
        {
            throw new ArgumentOutOfRangeException(nameof(where), where, "That is not a place.");
        }

        JsonObject configuration = await ReadConfigurationAsync(cancellationToken)
            .ConfigureAwait(false);

        if (configuration["if"] is not JsonObject network)
        {
            network = [];
            configuration["if"] = network;
        }

        if (network["ntp"] is not JsonObject ntp)
        {
            ntp = [];
            network["ntp"] = ntp;
        }

        // Without this the controller never learns the time, and a sun timer on a controller that
        // does not know the date fires whenever its uptime clock happens to say.
        ntp["en"] = true;
        ntp["lt"] = where.Latitude;
        ntp["ln"] = where.Longitude;
        ntp["tz"] = timeZone;

        using var content = new StringContent(
            configuration.ToJsonString(), System.Text.Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await _http
            .PostAsync("json/cfg", content, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new WledHttpException(
                response.StatusCode,
                $"That controller would not take the location ({(int)response.StatusCode}). " +
                "A settings PIN will block this.");
        }
    }

    /// <summary>
    /// Makes the controller's LED outputs as long as the runs plugged into them.
    /// <para>
    /// The direction that took a while to see: a controller's configured length is not a limit the
    /// runs have to fit inside, it is their sum. If you counted eight LEDs on the porch then that
    /// output has eight more LEDs on it than the setting says, and the setting is what is stale.
    /// WLED clamps any segment to the total, so until this is written the difference sits dark.
    /// </para>
    /// <para>
    /// Writes nothing when the controller already agrees. Flash has a finite number of writes in
    /// it, and saving a layout that changed nothing here is not a reason to spend one.
    /// </para>
    /// </summary>
    /// <returns>True when the controller was actually written to.</returns>
    public async Task<bool> SetLedOutputLengthsAsync(
        IReadOnlyList<int> lengths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lengths);

        JsonObject configuration = await ReadConfigurationAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!LedOutputWriter.Apply(configuration, lengths))
        {
            return false;
        }

        using var content = new StringContent(
            configuration.ToJsonString(), System.Text.Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await _http
            .PostAsync("json/cfg", content, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new WledHttpException(
                response.StatusCode,
                $"That controller would not take the LED output lengths ({(int)response.StatusCode}). " +
                "A settings PIN will block this.");
        }

        return true;
    }

    /// <summary>
    /// Changes one LED output's electrical settings - colour order, current budget, reversal,
    /// skipped LEDs, off-refresh - and nothing else about the controller.
    /// </summary>
    /// <returns>True when the controller was actually written to.</returns>
    public async Task<bool> SetLedOutputSettingsAsync(
        int index,
        LedOutputSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        JsonObject configuration = await ReadConfigurationAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!LedOutputWriter.ApplySettings(configuration, index, settings))
        {
            return false;
        }

        using var content = new StringContent(
            configuration.ToJsonString(), System.Text.Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await _http
            .PostAsync("json/cfg", content, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new WledHttpException(
                response.StatusCode,
                $"That controller would not take the output settings ({(int)response.StatusCode}). " +
                "A settings PIN will block this.");
        }

        return true;
    }

    /// <summary>How many clock entries a controller keeps, before the two sun ones.</summary>
    public const int ClockSlots = 8;

    private static JsonObject Write(ScheduledChange entry) => new()
    {
        ["en"] = entry.Enabled ? 1 : 0,
        ["hour"] = entry.Hour,
        ["min"] = entry.Minute,
        ["macro"] = entry.PresetId,
        ["dow"] = entry.DaysOfWeek,
        ["start"] = new JsonObject { ["mon"] = 1, ["day"] = 1 },
        ["end"] = new JsonObject { ["mon"] = 12, ["day"] = 31 },
    };

    /// <summary>
    /// An unused slot. Hour, minute and preset all zero is how the controller recognises one as
    /// empty and leaves it out when it writes its configuration back.
    /// </summary>
    private static JsonObject Empty() => new()
    {
        ["en"] = 0,
        ["hour"] = 0,
        ["min"] = 0,
        ["macro"] = 0,
        ["dow"] = 0,
        ["start"] = new JsonObject { ["mon"] = 1, ["day"] = 1 },
        ["end"] = new JsonObject { ["mon"] = 12, ["day"] = 31 },
    };

    /// <summary>
    /// The sunrise or sunset slot, written whether or not it is in use - its position is the only
    /// thing that says which end of the day it belongs to, so it cannot be left out.
    /// </summary>
    private static JsonObject WriteSun(IReadOnlyList<ScheduledChange> schedule, SunTrigger which)
    {
        ScheduledChange? entry = schedule.FirstOrDefault(e => e.Sun == which);

        return new JsonObject
        {
            ["en"] = entry?.Enabled == true ? 1 : 0,
            ["hour"] = 255,
            ["min"] = 0,
            ["macro"] = entry?.PresetId ?? 0,
            ["dow"] = entry?.DaysOfWeek ?? 0x7F,
        };
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

    /// <summary>
    /// Tells the controller to take its audio from the network, or to stop.
    /// </summary>
    /// <remarks>
    /// The AudioReactive usermod's sync has three settings: off, send what this box hears, and
    /// receive what another box sends. Off is the default, and a controller that is off ignores
    /// every packet sent to it however well formed.
    /// <para>
    /// Read back rather than trusted, the way the rename above is, because a settings PIN blocks
    /// this endpoint and a blocked write still answers 200.
    /// </para>
    /// </remarks>
    /// <returns>What the mode was before, so the caller can put it back.</returns>
    public async Task<int> SetSoundSyncAsync(
        bool receive, CancellationToken cancellationToken = default)
    {
        int before = await ReadSoundSyncAsync(cancellationToken).ConfigureAwait(false);
        int wanted = receive ? 2 : 0;

        if (before == wanted)
        {
            return before;
        }

        // Built rather than written out, because the one setting sits four objects deep and the
        // closing braces outnumber anything a string literal can say plainly.
        var patch = new JsonObject
        {
            ["um"] = new JsonObject
            {
                ["AudioReactive"] = new JsonObject
                {
                    ["sync"] = new JsonObject
                    {
                        ["port"] = WledAudioSync.DefaultPort,
                        ["mode"] = wanted,
                    },
                },
            },
        };

        using var body = new StringContent(patch.ToJsonString(), Encoding.UTF8, "application/json");

        using HttpResponseMessage write = await _http.PostAsync("json/cfg", body, cancellationToken)
            .ConfigureAwait(false);

        if (!write.IsSuccessStatusCode)
        {
            throw new WledHttpException(
                write.StatusCode,
                $"The controller refused the sound sync setting ({(int)write.StatusCode}).");
        }

        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);

        int applied = await ReadSoundSyncAsync(cancellationToken).ConfigureAwait(false);

        if (applied != wanted)
        {
            throw new WledException(
                "The controller did not take the sound sync setting. A settings PIN will block this.");
        }

        return before;
    }

    /// <summary>Which of the three sync settings the usermod is on: 0 off, 1 send, 2 receive.</summary>
    public async Task<int> ReadSoundSyncAsync(CancellationToken cancellationToken = default)
    {
        using JsonDocument config = await GetRawAsync(cancellationToken).ConfigureAwait(false);

        return config.RootElement.TryGetProperty("um", out JsonElement usermods) &&
               usermods.TryGetProperty("AudioReactive", out JsonElement audio) &&
               audio.TryGetProperty("sync", out JsonElement sync) &&
               sync.TryGetProperty("mode", out JsonElement mode) &&
               mode.TryGetInt32(out int which)
            ? which
            : 0;
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
