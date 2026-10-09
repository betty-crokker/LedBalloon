# LedBalloon

A cross-platform desktop app for two Gledopto GL-C-616WL controllers running WLED 0.15.3 on a house.

The hard-won knowledge about measuring effects against hardware — what the capture tool does to the
other segments, why a batch sweep is not N captures, which of the three frame times you want, why a
single capture of a random effect is not evidence — lives in **`docs/measuring-effects.md`**, because
it is true for anybody working on this and not only for the person whose house it is. Read that
before changing anything about how an effect is drawn.

What stays here is the part that is only true of this house.

## The controllers

| Name  | Address       | LEDs | Notes |
|-------|---------------|------|-------|
| South | 192.0.2.12 | 310  | Roofline is segment 2, LEDs 25-309. The one to test on. |
| North | 192.0.2.11 | 356  | **Leave alone.** Its configuration got muddled at some point and untangling it is a separate job. |

Both run at a configured 42 frames a second (`hw.led.fps`). They spent a long time at 0, which
uncaps the rate and had them rendering flat out at 96 and 107. That is the symptom of a partial
configuration post: WLED's handler assigns `strip.setTargetFps(hw_led["fps"])` without checking the
key is present, so a document that omits it reads as zero. If you post a configuration, post the
whole document. Colour blending (`hw.led.cb`) is 0 on both.

## Two rules that are not negotiable

- **Leave the lights off.** Every capture ends with everything dark, every segment, and any timed
  preset activations disabled. `tools/wled.cs` does this itself; if you drive a controller another
  way, do it yourself.
- **South only.** North is not to be changed, configured or lit.

## Testing against the hardware

Expected and encouraged — it is how every effect here has been checked, and it has caught bugs that
no amount of reading the firmware would have. Daytime is preferred: the lights are on a house, and
running test patterns is less noticeable to passers-by and neighbours in daylight.

```bash
dotnet run tools/wled.cs -- state 192.0.2.12
dotnet run tools/wled.cs -- capture 192.0.2.12 seg=2 from=25 to=309 fx=73 sx=0 ix=128 pal=11 ms=12000
dotnet run tools/wled.cs -- off 192.0.2.12
```

`native/against-house-all.py` captures every effect off south twice and compares both runs against
the engine.

## Effects are not written here any more

`native/` compiles WLED's own source and the app calls it — see `native/README.md`. There used to be
118 hand-written ports with a test each; they are gone.
