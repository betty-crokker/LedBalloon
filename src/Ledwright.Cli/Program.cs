using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Ledwright.Core;
using Ledwright.Core.Discovery;
using Ledwright.Core.Json;
using Ledwright.Core.Layout;
using Ledwright.Core.Models;

// A deliberately small, dependency-free CLI. It exists to prove the library against real hardware
// and to answer "what does my controller actually report?" without opening the GUI.

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    PrintUsage();
    return 0;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

try
{
    return await RunAsync(args, cancellation.Token);
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Console.WriteLine();
    return 130;
}
catch (TaskCanceledException)
{
    // Not a Ctrl+C: HttpClient reports its own timeout this way.
    Error("The device did not answer in time.");
    return 1;
}
catch (WledException ex)
{
    Error(ex.Message);
    return 1;
}
catch (HttpRequestException ex)
{
    Error($"Could not reach the device: {ex.Message}");
    return 1;
}

async Task<int> RunAsync(string[] argv, CancellationToken ct)
{
    string command = argv[0].ToLowerInvariant();

    if (command is "discover")
    {
        return await DiscoverAsync(ct);
    }

    if (command is "photo")
    {
        return Photo(argv[1..]);
    }

    if (argv.Length < 2)
    {
        Error($"'{command}' needs a device host, e.g. ledwright {command} 192.168.1.50");
        return 2;
    }

    string host = argv[1];
    string[] rest = argv[2..];

    using var client = new WledClient(host);

    switch (command)
    {
        case "info":
            return await InfoAsync(client, ct);
        case "state":
            return Dump(await client.GetStateAsync(ct), WledJson.Default.WledState);
        case "on":
            return await ApplyAsync(client, new WledState { On = true }, "on", ct);
        case "off":
            return await ApplyAsync(client, new WledState { On = false }, "off", ct);
        case "toggle":
            return await ToggleAsync(client, ct);
        case "bri":
            return await BrightnessAsync(client, rest, ct);
        case "color":
            return await ColorAsync(client, rest, ct);
        case "effect":
            return await EffectAsync(client, rest, ct);
        case "effects":
            return List(await client.GetEffectsAsync(ct), "effects");
        case "palettes":
            return List(await client.GetPalettesAsync(ct), "palettes");
        case "presets":
            return await PresetsAsync(client, ct);
        case "preset":
            return await ApplyPresetAsync(client, rest, ct);
        case "audit":
            return await AuditAsync(client, host, ct);
        case "config":
            return await ConfigAsync(host, ct);
        case "watch":
            return await WatchAsync(host, ct);
        default:
            Error($"Unknown command '{command}'.");
            PrintUsage();
            return 2;
    }
}

async Task<int> DiscoverAsync(CancellationToken ct)
{
    Console.WriteLine("Browsing for _wled._tcp for 5 seconds...");
    Console.WriteLine();

    var discovery = new ZeroconfWledDiscovery();
    int found = 0;

    await foreach (WledDiscoveryResult device in discovery.DiscoverAsync(TimeSpan.FromSeconds(5), ct))
    {
        found++;
        Console.WriteLine($"  {device.Name,-28} {device.ConnectHost}");
    }

    Console.WriteLine();
    Console.WriteLine(found == 0
        ? "Nothing found. mDNS is best-effort and does not cross subnets or most VPNs; try the IP directly."
        : $"{found} device(s).");

    return 0;
}

async Task<int> InfoAsync(WledClient client, CancellationToken ct)
{
    WledInfo? info = await client.GetInfoAsync(ct);
    if (info is null)
    {
        Error("No info returned.");
        return 1;
    }

    string websocket = info.WebSocketClients is -1 or null
        ? "disabled"
        : $"{info.WebSocketClients} client(s)";

    Console.WriteLine($"  Name       {info.Name}");
    Console.WriteLine($"  Firmware   {info.Version} (build {info.BuildId})");
    Console.WriteLine($"  Hardware   {info.Architecture}{(info.IsEsp8266 ? "  (go easy on it)" : string.Empty)}");
    Console.WriteLine($"  LEDs       {info.Leds?.Count} at {info.Leds?.Fps} fps");
    Console.WriteLine($"  Power      {info.Leds?.PowerMilliamps} mA of {info.Leds?.MaxPowerMilliamps} mA");
    Console.WriteLine($"  Effects    {info.EffectCount}");
    Console.WriteLine($"  Palettes   {info.PaletteCount}");
    Console.WriteLine($"  WebSocket  {websocket}");
    Console.WriteLine($"  Realtime   {(info.Live == true ? $"live from {info.LiveSource} {info.LiveSourceIp}" : "idle")}");
    Console.WriteLine($"  Uptime     {TimeSpan.FromSeconds(info.UptimeSeconds ?? 0)}");

    return 0;
}

async Task<int> ToggleAsync(WledClient client, CancellationToken ct)
{
    WledState? current = await client.GetStateAsync(ct);
    bool next = current?.On != true;
    return await ApplyAsync(client, new WledState { On = next }, next ? "on" : "off", ct);
}

async Task<int> BrightnessAsync(WledClient client, string[] rest, CancellationToken ct)
{
    if (rest.Length == 0 || !byte.TryParse(rest[0], out byte brightness))
    {
        Error("Usage: ledwright bri <host> <0-255>");
        return 2;
    }

    return await ApplyAsync(client, new WledState { Brightness = brightness }, $"brightness {brightness}", ct);
}

async Task<int> ColorAsync(WledClient client, string[] rest, CancellationToken ct)
{
    if (rest.Length == 0 || !RgbColor.TryParse(rest[0], out RgbColor color))
    {
        Error("Usage: ledwright color <host> <#rrggbb> [segment]");
        return 2;
    }

    int segment = rest.Length > 1 && int.TryParse(rest[1], out int parsed) ? parsed : 0;
    WledState patch = WledState.ForSegment(segment, s => s.PrimaryColor = color);
    patch.On = true;

    return await ApplyAsync(client, patch, $"segment {segment} to {color}", ct);
}

async Task<int> EffectAsync(WledClient client, string[] rest, CancellationToken ct)
{
    if (rest.Length == 0)
    {
        Error("Usage: ledwright effect <host> <index or name> [segment]");
        return 2;
    }

    string[] effects = await client.GetEffectsAsync(ct) ?? [];
    int index;

    if (int.TryParse(rest[0], out int parsed))
    {
        index = parsed;
    }
    else
    {
        index = Array.FindIndex(effects, e => e.Contains(rest[0], StringComparison.OrdinalIgnoreCase));

        if (index < 0)
        {
            Error($"No effect matching '{rest[0]}'. Run: ledwright effects <host>");
            return 1;
        }
    }

    int segment = rest.Length > 1 && int.TryParse(rest[1], out int seg) ? seg : 0;
    string name = index >= 0 && index < effects.Length ? effects[index] : index.ToString();

    return await ApplyAsync(
        client,
        WledState.ForSegment(segment, s => s.Effect = index),
        $"effect {index} ({name})",
        ct);
}

async Task<int> PresetsAsync(WledClient client, CancellationToken ct)
{
    IReadOnlyList<WledPreset> presets = await client.GetPresetsAsync(ct);

    if (presets.Count == 0)
    {
        Console.WriteLine("No presets stored on this device.");
        return 0;
    }

    Console.WriteLine($"{presets.Count} preset(s) from /presets.json:");
    Console.WriteLine();

    foreach (WledPreset preset in presets)
    {
        string kind = preset.IsPlaylist ? "playlist" : $"{preset.Segments?.Count ?? 0} seg";
        string quick = string.IsNullOrWhiteSpace(preset.QuickLabel) ? string.Empty : $"  [{preset.QuickLabel}]";
        Console.WriteLine($"  {preset.Id,3}  {preset.DisplayName,-30} {kind}{quick}");
    }

    return 0;
}

async Task<int> ApplyPresetAsync(WledClient client, string[] rest, CancellationToken ct)
{
    if (rest.Length == 0 || !int.TryParse(rest[0], out int id))
    {
        Error("Usage: ledwright preset <host> <slot>");
        return 2;
    }

    return await ApplyAsync(client, new WledState { Preset = id }, $"preset {id}", ct);
}

async Task<int> AuditAsync(WledClient client, string host, CancellationToken ct)
{
    WledInfo? info = await client.GetInfoAsync(ct);
    int ledCount = info?.Leds?.Count ?? 0;

    if (ledCount <= 0)
    {
        Error("The device did not report an LED count.");
        return 1;
    }

    IReadOnlyList<WledPreset> presets = await client.GetPresetsAsync(ct);
    IReadOnlyList<PresetGap> gaps = PresetAudit.FindGaps(presets, ledCount);

    Console.WriteLine($"{host} drives {ledCount} LEDs across {presets.Count} preset(s).");
    Console.WriteLine();

    if (gaps.Count == 0)
    {
        Console.WriteLine("  Every preset covers the whole strip.");
        return 0;
    }

    Console.WriteLine($"  {gaps.Count} preset(s) would leave LEDs dark:");
    Console.WriteLine();

    foreach (PresetGap gap in gaps)
    {
        Console.WriteLine($"    {gap}");
    }

    Console.WriteLine();
    Console.WriteLine("  These presets baked in the segment bounds that were current when they were saved.");
    Console.WriteLine("  Re-save them from Ledwright to refit them to the strip as it is now.");

    return 0;
}

async Task<int> ConfigAsync(string host, CancellationToken ct)
{
    var config = new WledConfigClient(host);
    IReadOnlyList<LedBus> buses = await config.GetLedBusesAsync(ct);

    if (buses.Count == 0)
    {
        Console.WriteLine("No LED outputs found in /cfg.json. A settings PIN will block this endpoint.");
        return 1;
    }

    Console.WriteLine($"{buses.Count} LED output(s) from /cfg.json:");
    Console.WriteLine();

    int index = 0;
    foreach (LedBus bus in buses)
    {
        Console.WriteLine($"  Output {index++}: {bus}{(bus.Reversed ? " reversed" : string.Empty)}");
    }

    Console.WriteLine();
    Console.WriteLine($"  Total configured: {buses.Max(b => b.StopExclusive)} LEDs");

    return 0;
}

async Task<int> WatchAsync(string host, CancellationToken ct)
{
    await using var device = new WledDevice(host);

    device.StateChanged += (_, state) =>
    {
        WledSegment? segment = state.MainOrFirstSegment();
        Console.WriteLine(
            $"[{DateTime.Now:HH:mm:ss}] on={state.On} bri={state.Brightness} " +
            $"fx={segment?.Effect} pal={segment?.Palette} col={segment?.PrimaryColor}");
    };

    device.Faulted += (_, ex) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {ex.Message}");

    await device.ConnectAsync(ct);

    Console.WriteLine($"Watching {device.DisplayName}. Change the lights from anywhere; Ctrl+C to stop.");
    Console.WriteLine();

    await Task.Delay(Timeout.Infinite, ct);
    return 0;
}

async Task<int> ApplyAsync(WledClient client, WledState patch, string description, CancellationToken ct)
{
    await client.ApplyAsync(patch, ct);
    Console.WriteLine($"Set {description}.");
    return 0;
}

int Photo(string[] rest)
{
    if (rest.Length == 0)
    {
        Error("Usage: ledwright photo <file> [budget KB]");
        return 2;
    }

    string path = rest[0];
    if (!File.Exists(path))
    {
        Error($"No such file: {path}");
        return 1;
    }

    int budgetKb = rest.Length > 1 && int.TryParse(rest[1], out int kb) ? kb : 400;
    byte[] source = File.ReadAllBytes(path);

    PreparedPhoto prepared = PhotoPreparer.Prepare(source, budgetKb * 1024);

    Console.WriteLine($"  Original   {source.Length / 1024} KB");
    Console.WriteLine($"  Prepared   {prepared.Jpeg.Length / 1024} KB");
    Console.WriteLine($"  Size       {prepared.Width} x {prepared.Height} at quality {prepared.Quality}");
    Console.WriteLine($"  Budget     {budgetKb} KB");
    Console.WriteLine($"  Hash       {prepared.Hash}");
    Console.WriteLine();
    Console.WriteLine(prepared.FitsBudget
        ? "  Fits. This photo can live on the controllers with the layout."
        : "  Too large even at the lowest setting. Crop it before using it.");

    if (rest.Length > 2)
    {
        File.WriteAllBytes(rest[2], prepared.Jpeg);
        Console.WriteLine($"  Written to {rest[2]}");
    }

    return prepared.FitsBudget ? 0 : 1;
}

int List(string[]? items, string label)
{
    if (items is null or { Length: 0 })
    {
        Error($"No {label} returned.");
        return 1;
    }

    Console.WriteLine($"{items.Length} {label} reported by this firmware:");
    Console.WriteLine();

    for (int i = 0; i < items.Length; i++)
    {
        Console.WriteLine($"  {i,3}  {items[i]}");
    }

    return 0;
}

int Dump<T>(T? value, JsonTypeInfo<T> typeInfo)
{
    if (value is null)
    {
        Error("Nothing returned.");
        return 1;
    }

    var pretty = new JsonSerializerOptions(typeInfo.Options) { WriteIndented = true };
    var prettyInfo = (JsonTypeInfo<T>)pretty.GetTypeInfo(typeof(T));

    Console.WriteLine(JsonSerializer.Serialize(value, prettyInfo));
    return 0;
}

void Error(string message)
{
    ConsoleColor before = Console.ForegroundColor;
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine(message);
    Console.ForegroundColor = before;
}

void PrintUsage()
{
    Console.WriteLine("""
        ledwright - a command-line WLED client

        USAGE
          ledwright <command> [host] [args]

        DISCOVERY
          discover                        browse the LAN for WLED devices over mDNS

        READING
          info      <host>                firmware, LED count, power draw, live source
          state     <host>                the full /json/state document
          effects   <host>                effect names, in fx index order
          palettes  <host>                palette names, in pal index order
          presets   <host>                presets stored on the device, vendor ones included
          config    <host>                LED outputs and lengths, read from /cfg.json
          audit     <host>                find presets that no longer cover the whole strip

        WRITING
          on | off | toggle <host>
          bri       <host> <0-255>
          color     <host> <#rrggbb> [segment]
          effect    <host> <index|name> [segment]
          preset    <host> <slot>

        LIVE
          watch     <host>                follow state changes from any source, until Ctrl+C

        EXAMPLES
          ledwright discover
          ledwright audit 192.168.1.50
          ledwright color wled-a1b2c3.local "#ff6600"
          ledwright effect 192.168.1.50 twinkle 1
        """);
}
