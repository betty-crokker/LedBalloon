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

## Porting effects

Each ported effect has a test in `tests/LedBalloon.Core.Tests/*AgainstHardwareTests.cs` carrying the
numbers measured on the strip alongside the numbers the port produces. The measured figures are
evidence and belong in the test, not in a commit message that scrolls away.

A few things learned the hard way, all of which have cost a rewrite at least once:

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
