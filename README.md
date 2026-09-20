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

**You cannot tell what a color will look like from the street.** So the app takes a photo of the
house at dusk, lets you draw each run onto it as a line, and paints the live colors back onto that
photo as you work.

### A line is not enough

Where a run is does not tell you what it looks like. Bare addressable pixels facing the road read as
a row of colored points; the same LEDs under an eave aimed at the wall are barely visible
themselves, and what you see from the street is the overlapping scallops they throw. Drawing the
second as though it were the first would make the preview confidently wrong, so each segment also
carries a **fixture**:

| Fixture | Drawn as |
| --- | --- |
| Addressable strip, facing out | One visible point of color per LED |
| Rope or diffused channel | A continuous line of glow, no discrete pixels |
| Downlights under an eave | Overlapping soft-edged cones washing down the wall |
| Uplights from the ground | The same, aimed up |

The aimed styles take a beam spread, a throw distance and an aim side — a line has two
perpendiculars and only you know which one is the wall. Beams are drawn as a few nested cones rather
than one triangle, because a real beam has no edge.

A line also does not say which way round the run goes, and getting that wrong runs every effect
backwards. The selected segment shows a filled marker at LED 1 and a hollow one at its last LED, and
*Flip which end is LED 1* swaps them.

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
| Lights: power, brightness, color, effects, palettes, segments, presets, playlists, nightlight, sync | `GET`/`POST /json/state` | yes |
| Device description: firmware, LED count, FPS, power, filesystem, usermods | `GET /json/info` | read-only |
| Effect and palette names | `GET /json/eff`, `/json/pal` | read-only |
| Everything behind the settings gear: network, wifi, ethernet, LED outputs, buttons, IR, relay, light behavior, boot defaults, sync, MQTT, Hue, Alexa, NTP, overlays, timers, OTA, usermods | `GET`/`POST /cfg.json` | yes, carefully |
| Presets and playlists | `GET /presets.json`, saved via `psave` in `/json/state` | yes |
| Files on the device | `/edit` | yes |

The settings *pages* are HTML forms posting to `/settings/*`, but the same configuration is
readable and writable whole as `/cfg.json` — that is what WLED's own backup and restore uses.

Genuinely outside JSON: firmware upload (`/update`), wifi scanning, and the live per-pixel preview,
which is a binary WebSocket stream rather than REST.

`Ledwright.Core` reads `/cfg.json`; it deliberately does not wrap writing it, because a malformed
post can leave a controller needing a factory reset. Use `WledConfigClient.GetRawAsync`, edit, and
post it back yourself once you have a backup.

## Giving it to someone else

`publish.ps1` produces a single self-contained executable with the .NET runtime bundled inside it.
The other machine needs nothing installed — no SDK, no runtime, no Visual Studio.

```powershell
.\publish.ps1
```

That writes `publish/win-x64/Ledwright.App.exe`, about 48 MB. Copy that one file anywhere and run
it. Pass `-Runtime linux-x64` or `-Runtime osx-arm64` to build for the other platforms; you can
cross-publish all of them from Windows.

Two things to expect: Windows SmartScreen warns the first time because the file is not code-signed
(More info → Run anyway), and the machine has to be on the same network segment as the controllers,
since mDNS does not cross subnets.

## The app is about the house, not the hardware

Controllers are a setup concern. You find them once, give each one a name, describe the runs of LED
hanging off them, and then they get out of the way:

- **Segments** are the permanent left-hand panel — the gable, the porch rail, whatever you hung.
- **Presets** from every controller are merged into one list, de-duplicated by name. If both boxes
  store "Winter both" it appears once and recalling it sends the right slot number to each, even
  when the slot numbers differ. A preset only one controller has is still listed, and applying it
  says so rather than silently lighting half the house.
- **Controllers** live in a Setup tab and a quiet "2 of 2 controllers connected" in the status bar.

A house is not a controller: segments carry the MAC of the box that drives them, so a project spans
as many controllers as the wiring needs, each with its own LED address space.

## Identifying controllers

Controllers are filed by **MAC address**, not IP. The MAC survives a DHCP lease moving the device,
and it is unique where the friendly name is not — both development controllers ship as
"WLED-Gledopto". WLED also derives its mDNS name from the MAC (`wled-6a1be8.local`), so discovery
finds the same device at whatever address it landed on.

## Where settings live: on the controllers

The layout is stored on the controllers themselves, as `ledwright.json` on their flash, **mirrored
to every one of them**. Describe the house once on one machine, press *Save to controllers*, and
anyone else on the same network opens Ledwright and finds it already set up. Nothing is copied
between machines, nothing lives on anyone's disk, and there is no file to keep in sync.

Mirroring rather than splitting means any single controller is enough to rebuild the house, so one
unplugged for the winter does not take the layout with it. Copies can drift if someone saves while a
controller is offline, so each save bumps a revision number; on load the highest revision wins and
the disagreement is reported rather than silently resolved.

Verified end to end against the two development controllers: save wrote an identical 947-byte file
to both, and a cold restart reported *"Loaded revision 1 from WLED-Gledopto — 2 segments, 310 LEDs"*
and opened on the House rather than on Setup.

**One firmware quirk worth knowing.** WLED's file editor answers **HTTP 500 to a perfectly
successful upload** — confirmed on 0.15.3, where the file read back byte for byte after the error.
So `WledFileSystemClient` does not trust the status code: it reads the file back and checks the
length before reporting success. Anything built against `/edit` needs to do the same, or it will
report failure on every write that worked.

`FileProjectStore` remains for controllers with no room to spare; `WledCapabilities.CanStoreProjectOnDevice`
decides. Flash has a finite write budget, so saving is an explicit action, not something that
happens on every edit.

### The photo, and why no file path is ever stored

The photo travels with the project, because a path does not. A path that works on one machine is
meaningless on another, and asking two people to keep a file in the same place stops working the
first time someone tidies their Downloads folder. So a photo is identified by a hash of its
contents, never by where it sits.

`PhotoPreparer` scales it to 1600px and re-encodes as JPEG, giving up quality before resolution
until it fits. A 3.7 MB phone photo of the development house came out at **389 KB, 1600x1200**, and
uploading it to a controller took about two seconds and read back byte for byte.

The budget is **measured, not assumed**: `PhotoPreparer.BudgetFor` takes the free space each
controller reports, uses the smallest, and leaves 250 KB of headroom for firmware and presets. A
cramped ESP8266 build simply yields a budget of zero and no photo goes on the device — nothing here
is tuned to the hardware it was developed on.

When the photo will not fit, the layout still syncs and only the picture stays behind. Each machine
keeps a `PhotoCache` under its local app data, filed by hash. Whoever has the file loads it once;
anyone else is told the layout expects a photo and picks the same file once. Because the hash
identifies it, picking the wrong picture is detected rather than silently drawn on.

One caveat: the free-space figures in `/json/info` appear not to refresh immediately after a write,
so the budget can be computed from slightly stale numbers. The headroom absorbs it.

```bash
ledwright photo "C:\path\to\house.jpg" 680
```

reports what a photo would shrink to and whether it fits, without touching anything.

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

**Partial updates.** `WledState` has nullable properties throughout and serializes with
`JsonIgnoreCondition.WhenWritingNull`, so the same type is both a full snapshot and a sparse patch.
`new WledState { On = true }` serializes to exactly `{"on":true}` and touches nothing else.

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

- Looks are modeled, resolved and tested, but the app has no UI for saving and recalling them.
  The whole layout-aware preset mechanism is built and unreachable.
- The photo canvas paints each segment in its segment color; it does not yet animate effects.
  WLED can stream real per-pixel data over the WebSocket live-preview channel, which would make the
  preview exact — the frame format needs verifying against the firmware first.
- Storing the photo on the controllers is implemented and the downscaling is in place, but the
  round trip has not been exercised with a real photo.
- Renaming a controller on the device itself (`/cfg.json` write) has not been run against hardware.
- No CI workflow, and no packaged installers.

## License

MIT.
