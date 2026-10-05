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
  Once saved, the scene joins the list of options the device reports, so other software can find it
  and play it — see [picking a scene from code](#picking-a-scene-from-code) for what that actually
  involves.
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

### Picking a scene from code

The name is a label for people; the wire carries numbers.

Ask the device what it has, with `GET /router/api/v1/device/scenes` for the built-in ones or
`GET /router/api/v1/device/diy-scenes` for yours. Each option comes back shaped like this:

```json
{ "name": "Sunrise", "value": { "paramId": 4280, "id": 3853 } }
```

To play it, POST to `/router/api/v1/device/control` with the whole `value` object:

```json
{
  "capability": {
    "type": "devices.capabilities.dynamic_scene",
    "instance": "diyScene",
    "value": { "paramId": 4280, "id": 3853 }
  }
}
```

`instance` is `lightScene` for Govee's own effects and `diyScene` for yours.

Two consequences worth planning for:

- **You cannot hard-code the numbers.** They belong to that model and that account, and a DIY scene
  edited in the app can come back with different ones. Fetch the list, match on the name, send the
  value you were given.
- **A renamed scene breaks anything matching on the name.** The ids are the identity; the name is
  how a person finds it. If something has to survive renaming, store the id.

This is what Home Assistant is doing when a Govee light offers its scenes as a dropdown: it fetched
the list, and the names you see are the labels from it.

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

## How the Govee app itself talks to the lights

Worth knowing, because it decides what is worth reverse engineering.

The app uses **three** transports, and the cloud is the main one:

1. **Govee's servers** — `app2.govee.com` for the account, the device list and the scene
   catalogues, plus an **AWS IoT MQTT** connection for low-latency control and status. Most of what
   the app does goes out to the internet and back, even when the phone is standing next to the
   lights.
2. **Bluetooth LE** — direct to the control box. Used for setup and for control while in range.
3. **LAN UDP** — only when LAN Control is switched on for that device, and only the thin command set.

So "the app talks to my lights" is mostly false. It talks to Govee, and Govee talks to the lights.

## Reverse engineering: what has already been done

A good deal, and several projects depend on it today.

- **The app's own scene catalogue is readable.** A request to
  `https://app2.govee.com/appsku/v1/light-effect-libraries?sku=H7020` with an `AppVersion`
  header returns the scenes for that model, including their **scene IDs and scene codes** — the
  numbers the app sends to select an effect. This is how integrations offer effects the official API
  never exposed.
- **The AWS IoT MQTT interface** has been worked out and is what `govee2mqtt` uses for fast
  status and Tap-to-Run scenes. It needs the Govee account email and password, not just an API key.
- **The BLE protocol** is documented for several models by community projects: packets beginning
  `0x33`, with `0x05` for colour mode, `0x04` for selecting a scene and `0x0a` for
  **DIY mode**. So a DIY scene can be *invoked* over Bluetooth by its code, with no cloud involved.

What nobody appears to have published is **creating** a DIY scene from outside the app — defining
one and saving it. There is no evident technical barrier: it is an authenticated HTTP call to
`app2.govee.com` like any other, and someone with a proxy and some patience could capture it. It
simply does not seem to have been done and shared.

### The catch, and it is a live one

The unofficial paths break. Govee has changed the app API more than once and ships a check that
rejects old clients outright — `govee2mqtt` has had crash-on-startup reports with the server
answering *"The app version is too low, please upgrade the version!"*, which takes out every
integration built on that endpoint until a maintainer updates the version string.

The documented Developer API is the only path with anything behind it. Everything else works until
Govee changes something, with no notice and no obligation.

### So is it worth doing?

Depends what you want.

- **To play existing scenes, including DIY ones, locally and fast** — it is already done for you.
  Use `govee2mqtt` and accept the occasional breakage.
- **To get effects the lights do not already have** — no. Even perfectly reverse engineered, the
  firmware's effect engine is a fixed list and a DIY scene is a parameter set for it, so the ceiling
  is the same. Driving `segmentedColorRgb` yourself gives more freedom than any scene does, and
  that is documented and supported.
- **To get off the cloud entirely** — partly. BLE and LAN both work without it, but neither offers
  per-segment colour, and BLE needs something sitting in Bluetooth range of the control box.

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
- [Govee-Reverse-Engineering - computing scene codes](https://github.com/egold555/Govee-Reverse-Engineering/issues/11)
- [govee_h7015 - BLE protocol notes](https://github.com/ConsciousCode/govee_h7015)
- [H6127 BLE reverse engineering](https://github.com/BeauJBurroughs/Govee-H6127-Reverse-Engineering)
- [govee2mqtt #637 - the app API rejecting old clients](https://github.com/wez/govee2mqtt/issues/637)
- [Govee connector for Power Platform](https://learn.microsoft.com/en-us/connectors/govee/)
- [H7020 user manual](https://manuals.plus/govee/h7020-rgbic-warm-white-wifi-and-bluetooth-smart-outdoor-string-lights-manual)
