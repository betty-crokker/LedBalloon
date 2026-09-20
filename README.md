# Ledwright

A cross-platform desktop app and library for [WLED](https://kno.wled.ge) controllers, in C# on .NET 10.
Runs on Windows, Linux and macOS from one codebase.

Built and tested against two Gledopto ESP32 controllers running WLED 0.15.3, but nothing in it is
vendor-specific — every list, limit and capability is read from the device rather than assumed.

## Why this exists

Two annoyances with the stock web UI, both of which shape the design:

**Presets forget how long your runs are.** A WLED preset bakes in the segment `start`/`stop` that
were current when you saved it. Correct a run from 100 LEDs to 105 and the preset still says
`stop: 100`, so the last five stay dark. This is real and measurable — `ledwright audit` against the
development hardware found the boot preset covering 257 of 356 LEDs, leaving 99 unlit on every power-up.

Ledwright's own saved appearances, called **looks**, store no LED indices at all. They name segments;
the indices are resolved from the project at the moment the look is applied. Fix a length in one
place and every look follows.

**You cannot tell what a colour will look like from the street.** So the app takes a photo of the
house at dusk, lets you draw each run onto it as a line, and paints the live colours back onto that
photo as you work.

## Layout

```
src/Ledwright.Core    the library: HTTP, WebSocket, UDP, mDNS, layout model
src/Ledwright.Cli     a small console client, for proving things against real hardware
src/Ledwright.App     the Avalonia desktop app
tests/                xUnit tests, with sample documents captured from a real controller
```

## Quick start

```bash
dotnet build
dotnet test
```

```bash
dotnet run --project src/Ledwright.Cli -- discover
```

The CLI is the fastest way to see what your controller actually reports:

| Command | What it does |
| --- | --- |
| `discover` | browse the LAN over mDNS |
| `info <host>` | firmware, LED count, power draw, live source |
| `config <host>` | physical LED outputs and their lengths, from `/cfg.json` |
| `presets <host>` | presets stored on the device, vendor-preloaded ones included |
| `audit <host>` | find presets that no longer cover the whole strip |
| `watch <host>` | follow state changes from any source, live |
| `on`/`off`/`bri`/`color`/`effect`/`preset` | drive the lights |

## How much of WLED is reachable as JSON

Effectively all of it, spread across three documents. Verified against 0.15.3:

| What | Where | Writable |
| --- | --- | --- |
| Lights: power, brightness, colour, effects, palettes, segments, presets, playlists, nightlight, sync | `GET`/`POST /json/state` | yes |
| Device description: firmware, LED count, FPS, power, filesystem, usermods | `GET /json/info` | read-only |
| Effect and palette names | `GET /json/eff`, `/json/pal` | read-only |
| Everything behind the settings gear: network, wifi, ethernet, LED outputs, buttons, IR, relay, light behaviour, boot defaults, sync, MQTT, Hue, Alexa, NTP, overlays, timers, OTA, usermods | `GET`/`POST /cfg.json` | yes, carefully |
| Presets and playlists | `GET /presets.json`, saved via `psave` in `/json/state` | yes |
| Files on the device | `/edit` | yes |

The settings *pages* are HTML forms posting to `/settings/*`, but the same configuration is
readable and writable whole as `/cfg.json` — that is what WLED's own backup and restore uses.

Genuinely outside JSON: firmware upload (`/update`), wifi scanning, and the live per-pixel preview,
which is a binary WebSocket stream rather than REST.

`Ledwright.Core` reads `/cfg.json`; it deliberately does not wrap writing it, because a malformed
post can leave a controller needing a factory reset. Use `WledConfigClient.GetRawAsync`, edit, and
post it back yourself once you have a backup.

## Identifying controllers

Controllers are filed by **MAC address**, not IP. The MAC survives a DHCP lease moving the device,
and it is unique where the friendly name is not — both development controllers ship as
"WLED-Gledopto". WLED also derives its mDNS name from the MAC (`wled-6a1be8.local`), so discovery
finds the same device at whatever address it landed on.

## Where settings live

`IProjectStore` has two implementations:

- `DeviceProjectStore` writes `ledwright.json` to the controller's own flash. Nothing on the PC, so
  a fresh install finds its settings by discovering the device. The development controllers had
  ~935 KB of 983 KB free, which is room for the project and a downscaled photo.
- `FileProjectStore` keeps an ordinary local file, for devices too small to host their own settings.

The app picks based on `WledCapabilities.CanStoreProjectOnDevice` rather than assuming. Flash has a
finite write budget, so saves happen on explicit user action, not on every edit.

## The layout problem, and why the app has to ask

A WLED device stores **no single segment definition**. Every preset carries its own copy of the
bounds that were current when it was saved, so a controller with ten presets can hold ten different
opinions about where a run starts and ends.

`LayoutInference.Propose` weighs the evidence rather than guessing:

- Physical output boundaries from `/cfg.json` are hardware fact, and are always accepted.
  On the development controller that is two outputs: 308 LEDs on pin 16, 48 on pin 2.
- Boundaries most presets agree on are proposed as segment edges.
- Boundaries only a minority believe in are surfaced as **conflicts** for you to rule on.

Whatever you confirm becomes the project's single source of truth, and `PresetAudit.BuildRefit` can
then rewrite the device's existing presets to match.

## Design notes worth knowing

**Partial updates.** `WledState` has nullable properties throughout and serialises with
`JsonIgnoreCondition.WhenWritingNull`, so the same type is both a full snapshot and a sparse patch.
`new WledState { On = true }` serialises to exactly `{"on":true}` and touches nothing else.

**Source-generated JSON.** Not optional polish — it is what keeps trimmed and NativeAOT publishes
from failing at runtime.

**Rate limiting.** `StateCoalescer` merges queued patches field by field instead of queueing them,
so a dragged slider always sends the newest intent and never works through a backlog. Post on every
tick without thinking about it.

**Reads follow the device.** State comes from an HTTP snapshot and then WebSocket pushes, so the UI
stays correct when the lights are changed from the phone app or a wall button. Writes go over the
socket, falling back to HTTP while it reconnects.

**Terminology.** *Segment* is WLED's own word for an addressable run, and this app uses it too.
*Output* or *bus* is a physical IO port. In code, `Ledwright.Core.Layout.Segment` is the durable
definition with geometry; `Ledwright.Core.Models.WledSegment` is the wire format it resolves into.

## Not done yet

- Looks are modelled, resolved and tested, but the app has no UI for saving and recalling them.
- The photo canvas paints each segment in its segment colour; it does not yet animate effects.
  WLED can stream real per-pixel data over the WebSocket live-preview channel, which would make the
  preview exact — the frame format needs verifying against the firmware first.
- `WledFileSystemClient` writes are implemented but have not been exercised against a device.
- No CI workflow, and no packaged installers.

## Licence

MIT.
