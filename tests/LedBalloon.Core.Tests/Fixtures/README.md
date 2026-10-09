# Fixtures

Recordings from real hardware, kept verbatim. Nothing here needs a controller to run — that is the
point of recording them — but all of it came off one.

| File | What it is |
|---|---|
| `wled-0.15.3-two-outputs.json` | A whole `cfg.json` from a Gledopto GL-C-616WL on WLED 0.15.3, with two LED outputs: 25 LEDs on GPIO 16 and 285 on GPIO 2. |
| `*.txt` | Frames captured off a strip while one effect ran, one hex line per frame. |

## Why the configuration is a real one

Writing configuration to a controller is the most destructive thing this app does — a malformed
post can leave a box needing a factory reset — so the tests are as much about what the writer
**leaves alone** as about what it changes. A hand-written minimal document cannot show that. It
would not have the nested output array, the usermod block, the frame-rate key that WLED assigns
without checking whether it is there, or the forty other settings that have to come back untouched.

So it is a real one, and it stays shaped like a real one.

## What has been changed in it

Only the parts that identified a particular house rather than a particular piece of hardware: the
mDNS name and the MQTT client id and topic, which WLED derives from the device's MAC, and the
latitude and longitude. The coordinates are now round numbers in the time zone the rest of the file
declares, so the sun timers still make sense.

Everything else is exactly as the controller wrote it, including the wifi block — which carries no
credentials, because WLED does not put them in `cfg.json`. It reports `"pskl"`, the length of the
password, and nothing else.

## Adding one

Capture it, say in the table above what it is, and take out anything that says whose house it came
from. If a test needs the real shape of something, a recording is the right answer; if it only
needs three fields, write those three fields in the test where the reader can see them.
