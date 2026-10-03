# LedBalloon

A cross-platform desktop app for two Gledopto GL-C-616WL controllers running WLED 0.15.3 on a house.

## The controllers

| Name  | Address       | LEDs | Notes |
|-------|---------------|------|-------|
| South | 192.0.2.12 | 310  | Roofline is segment 2, LEDs 25-309. The one to test on. |
| North | 192.0.2.11 | 356  | **Leave alone.** Its configuration got muddled at some point and untangling it is a separate job. |

Both are set to unlimited frame rate (`hw.led.fps` is 0), which is why they run at wire
speed — about 107 frames a second on south and 96 on north — rather than at any configured
ceiling. Colour blending (`hw.led.cb`) is 0 on both.

## Driving them

Plain HTTP over the LAN, no browser needed:

```bash
dotnet run tools/wled.cs -- state 192.0.2.12
```

`tools/wled.cs` covers control and capture. Capture reads the strip back over WLED's live preview
socket, which is the one thing `curl` cannot do, and prints one hex line per frame ready to check in
as a fixture:

```bash
dotnet run tools/wled.cs -- capture 192.0.2.12 seg=2 from=25 to=309 fx=73 sx=0 ix=128 pal=11 ms=12000
```

**Capture switches every other segment off**, deliberately, so that what is measured is the one
segment asked for and nothing else. That makes it the wrong tool for any question about what a
second segment is doing while the first one changes - it will read black and the black is the
tool's doing, not the controller's. For those, query `/json/state` and look at whether the segment
is still in the table and still on: a segment that exists and is on is being rendered.

It turns the lights off when it finishes. So does `off`:

```bash
dotnet run tools/wled.cs -- off 192.0.2.12
```

## Testing against the hardware

Testing on the real strip is expected and encouraged - it is how every ported effect here has been
checked, and it has caught bugs that no amount of reading the firmware would have. Daytime is
preferred: the lights are on a house, and running test patterns is less noticeable to passers-by and
neighbours in daylight.

Two rules that are not negotiable:

- **Leave the lights off.** Every capture ends with everything dark, every segment, and the timed
  preset activations disabled. The tool does this itself; if you drive the controller another way,
  do it yourself.
- **South only.** North is not to be changed, configured or lit.

## Checking effects against the house

Effects are not written here any more. `native/` compiles WLED's own source and the app calls it -
see `native/README.md`. There used to be 118 hand-written ports with a test each; they are gone.

`native/against-house-all.py` captures every effect off south twice and compares both runs against
the engine. Measured figures are evidence and belong in a test or in that README, not in a commit
message that scrolls away.

A few things learned the hard way, all of which have cost a rewrite at least once:

- **Ask whether the house agrees with itself before blaming the engine.** Many effects accumulate
  state once per rendered frame from a beat function of the controller's absolute clock, which has
  been running for days - Colorwaves adds `duration * beatsin88_t(400,5,9)` to its hue every frame.
  Two captures of such an effect minutes apart differ, so nothing can match it from a single
  capture, the controller included. Capturing twice is cheap, and it turns "the engine is wrong"
  into "nothing could have been right".
- **A batch sweep is not N individual captures.** Colorwaves measures 1.33 against the engine
  captured on its own and 30.06 captured inside a 187-effect sweep. Sweeps of one and three effects
  are fine. Not the controller slowing down, not the previous effect's state, not the clock origin -
  all three ruled out by measurement, cause still unknown. Re-capture anything that fails.
- **When the engine looks wrong, suspect `native/shim/` first.** Every bug found in it so far has
  been there, rather than in the vendored source or in the architecture: `map` written as a macro so
  that an unsigned subtraction underflowed, `pgm_read_dword` truncating a 64-bit pointer,
  `fastled_config.h` never included so three behaviour switches were off, and no `ARDUINO_ARCH_ESP32`
  so `MIN_FRAME_DELAY` fell through to the 8266's 8 instead of the ESP32's 2.

- **Brightness is usually the wrong signal.** An effect that blends one colour into another is
  brightest at *both* ends of its swing, so `max(r, g, b)` reports twice as many bands as there are
  and half the real period. Read the channel the effect actually drives.
- **A zero in a measurement is often the sample, not the effect.** Two assertions here have pinned a
  zero that was 170 frames of luck.
- **There are three frame times and they are all different.** The interval between frames (9 ms
  here), WLED's `FRAMETIME` macro (2 ms, from the configured rate), and `FRAMETIME_FIXED` (23 ms, a
  compile-time constant Candle asks for). Using one where another belongs is a silent four-fold
  error.
- **Match effects by name, never by number.** WLED's effect ids move between releases, and two of
  them here sit nowhere near where the source order suggests.
- **The clock an effect sees restarts when the effect does.** Which is why every simulation here
  starts at zero. Noise Pal proved it: it waits one full change interval before inventing its first
  palette, and a capture with no settling sits black for exactly that 5.28 seconds before lighting.
- **One capture of a random effect is not evidence.** Aurora's mean brightness came out 48.19, 46.75
  and 50.23 over three captures against a port that ranges 42 to 55 across seeds - so a single
  capture against a single seed can read as a fourteen percent error in either direction. Worse, its
  first capture had no saturated pixels at all and nearly went into a test as "never saturates"; the
  second had 0.62%.
- **Never write a test around something being absent.** Twice a test here used an unported effect
  as an example and broke the day it was ported, and the second time the comment on it already said
  so. The engine offers every effect that is not matrix-only, so there is nothing absent left to
  lean on.
- **`aux0` and `aux1` are sixteen bits wide, `step` is thirty-two.** The width is behaviour: Pacifica
  adds tens of thousands to its counters every frame and needs them to wrap at 65536 to stay
  periodic.
