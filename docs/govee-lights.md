# Controlling Govee lights from something other than the Govee app

Written for the **H7020 Outdoor String Lights** (48 ft, 15 RGBIC bulbs), but the shape of this
applies to most current Govee lights. Where a fact is specific to the H7020 it says so.

Not part of LedBalloon. It lives here because this is where the lighting notes are.

## The short version

- **The Govee Home app is the only place a DIY scene can be created.** Every other route can list
  and trigger the DIY scenes you have already made; none of them can define one.
- **Per-segment colour is available, but only through the cloud API.** That is the real escape hatch
  for anything the built-in effects do not do.
- **The local API is fast, private and thin**: power, brightness, colour, colour temperature. No
  effects, no segments.
- **The H7020 is Wi-Fi + Bluetooth.** It is not a Matter or Thread device; the Matter-capable
  outdoor string lights are the newer H702A/B/C. If Google Home reaches an H7020, it is going out
  to Govee's cloud and back, not over Thread.

## What each route can do

| | on/off + brightness | colour | colour temp | built-in effects | trigger DIY scenes | **create** DIY scenes | per-segment RGB | music mode | works with the internet down |
|---|---|---|---|---|---|---|---|---|---|
| Govee Home app | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ **only here** | ✅ | ✅ | partly (BLE) |
| Govee Developer API v2 (cloud) | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ | ✅ | ❌ |
| Govee LAN API (local UDP) | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ |
| Bluetooth LE (unofficial) | ✅ | ✅ | ✅ | partial | ❌ | ❌ | partial | ❌ | ✅ |
| Google Home / Alexa | ✅ | ✅ | ✅ | some, as named scenes | some | ❌ | ❌ | ❌ | ❌ |
| Matter (H702A/B/C, **not** H7020) | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ |
| HA `govee_light_local` | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ |
| HA `govee2mqtt` | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ | ✅ | partly |
| Homebridge `homebridge-govee` | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | partial | ✅ | partly |

"Partial" means some models only, or some of the capability.

## The Govee website

Worth being clear about, because it is the thing people go looking for and it is not there.

**There is no Govee web console for controlling your lights.** `us.govee.com` is the shop and the
support pages. `community.govee.com` is the forum. There is no browser page where you log in and
drive your own devices.

What the website is for:

- **`developer.govee.com`** — the API documentation, and the form that issues an API key. You can
  also request the key from inside the Govee Home app: *Profile → Settings → Apply for API Key*.
  The key arrives by email within a minute or so; check spam.
- **The API reference PDFs** (v1.5 and v2.0), linked below, which are the authoritative list of what
  each capability is called.
- **The supported-model list**, which is how you confirm a device is reachable at all. The H7020
  is on it.

Browser control of Govee lights exists only as third-party apps people have built on the cloud API.
There are several on GitHub; they are all someone's Node or Python app holding your API key.

## Govee Home app

The authoring surface, and the only one.

- **DIY scenes** are built here: *device → DIY → +*, where you lay out colours, movement and speed.
  Once saved, a DIY scene becomes an option every other route can select by name.
- **Segment control** — the H7020 is RGBIC, so its 15 bulbs are individually addressable and the app
  can colour them one at a time.
- **Music mode**, using the control box's own microphone.
- **Schedules and timers**, stored on the device.
- **LAN control switch** — *Device Settings → LAN Control*. This has to be turned on per device
  before anything local can talk to it.

## Govee Developer API v2 (cloud)

The one that gets past the predefined scenes. REST over HTTPS, authenticated with a
`Govee-API-Key` header.

```
GET  https://openapi.api.govee.com/router/api/v1/user/devices
POST https://openapi.api.govee.com/router/api/v1/device/control
```

**Start with the first one.** It returns each device's own capability list, which is the only
trustworthy answer to "what can this particular unit do" — more reliable than any table, including
the one above.

Capabilities worth knowing by name:

| capability | instances | what it does |
|---|---|---|
| `devices.capabilities.on_off` | `powerSwitch` | power |
| `devices.capabilities.range` | `brightness` | brightness |
| `devices.capabilities.color_setting` | `colorRgb`, `colorTemperatureK` | whole-device colour |
| `devices.capabilities.dynamic_scene` | `lightScene`, `diyScene`, `snapshot` | built-in effects, your DIY scenes, app snapshots |
| `devices.capabilities.segment_color_setting` | `segmentedColorRgb`, `segmentedBrightness` | **per-bulb colour and brightness** |
| `devices.capabilities.music_setting` | `musicMode` | music reactive mode |

`segment_color_setting` takes an array of segment indices plus a colour, so you can address one bulb
or a handful in a single call. This is how you build something the app does not offer — a chase, a
gradient, a colour per bulb driven by your own data.

**The cost:** every frame is a round trip to Govee's servers and back, and the API is rate limited.
Fine for a slow gradient, a per-minute update, or a one-shot arrangement. Useless for animation at
any real frame rate — and if Govee's cloud or your internet is down, nothing works.

**`dynamic_scene` is select-only.** Govee's own documentation says to build the scene in the app and
then read the options back through the interface. There is no endpoint that defines one.

## Govee LAN API (local UDP)

No cloud, no internet, low latency. Must be enabled per device in the app first.

- Control commands go to the device on **UDP 4003**; discovery is a multicast request and devices
  answer back on their own port. The protocol is a handful of JSON messages.
- **Supported commands: power, brightness, colour, colour temperature.** That is the whole list.
- No effects, no DIY scenes, no segment addressing.

Govee publishes this as a short document and maintains a supported-SKU list; the list in the PDF is
older than the integrations that use it, so a model missing from the PDF may still work.

## Bluetooth LE

Unofficial and reverse-engineered. Projects like `govee_ble_lights` drive some models directly,
including limited effect and segment support on certain SKUs. Useful when there is no Wi-Fi, or as a
fallback inside a bridge that also speaks LAN and cloud. Expect model-by-model variation and
occasional breakage when firmware changes.

## Home Assistant

Two realistic choices, and they trade off exactly the way the table above suggests.

**`govee_light_local`** — built in, no HACS, LAN only. Fast, private, keeps working when the
internet does not. H7020 is on its supported list. Gives you power, brightness and colour; no scenes.

**`govee2mqtt`** — a HACS add-on that speaks LAN, cloud **and** BLE at once, preferring whichever is
available. This is the one to use if the scenes matter: it exposes built-in scenes and your DIY
scenes as selectable options, and it is the project with the most active maintenance. It needs your
Govee account credentials and an API key.

The older `hacs-govee` (LaggAt) is cloud-only and largely superseded.

If the goal is "everything that Google Home gives me, plus the scenes, plus local control when the
cloud is down", `govee2mqtt` is the answer.

## Other bridges

- **`homebridge-govee`** — HomeKit, with documented support for scene, music and DIY modes.
- **Hubitat "Govee Integration V2"** — a community app covering the v2 API including scenes.
- **Power Platform** — Microsoft publishes a Govee connector for Power Automate, if the goal is
  "turn the porch orange when a thing happens in a spreadsheet".

## What cannot be done, by anything

- **Create or edit a DIY scene outside the Govee Home app.**
- **Run a custom effect on the device.** There is no user-programmable effect engine; the firmware's
  effect list is fixed. Anything custom has to be driven frame by frame from outside, over the cloud,
  within the rate limit.
- **Per-segment control without the cloud.** The LAN API does not expose segments.

That last pair is the real difference from a WLED-based setup, where the whole effect engine is on
the controller, open, and reachable over the local network with no account involved.

## Sources

- [Supported product models](https://developer.govee.com/docs/support-product-model)
- [Control your devices (API v2 reference)](https://developer.govee.com/reference/control-you-devices)
- [Developer API Reference v2.0 (PDF)](https://govee-public.s3.amazonaws.com/developer-docs/GoveeDeveloperAPIReference.pdf)
- [Developer API Reference v1.5 (PDF)](https://govee-public.s3.amazonaws.com/developer-docs/GoveeAPIReference.pdf)
- [LAN API 101 (Govee community)](https://community.govee.com/posts/mastering-the-lan-api-series-lan-api-101/136755)
- [Home Assistant — Govee Lights Local](https://www.home-assistant.io/integrations/govee_light_local/)
- [govee2mqtt](https://github.com/wez/govee2mqtt) · [DIY scene discussion](https://github.com/wez/govee2mqtt/issues/344)
- [homebridge-govee — Scene, Music, DIY modes](https://github.com/bwp91/homebridge-govee/wiki/Scene,-Music,-DIY-Modes)
- [govee_ble_lights](https://github.com/Beshelmek/govee_ble_lights)
- [Govee connector for Power Platform](https://learn.microsoft.com/en-us/connectors/govee/)
- [H7020 user manual](https://manuals.plus/govee/h7020-rgbic-warm-white-wifi-and-bluetooth-smart-outdoor-string-lights-manual)
