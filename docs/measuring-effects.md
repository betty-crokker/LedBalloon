# Measuring an effect against real hardware

CONTRIBUTING says measurement beats reading. This is what that costs in practice, and what it has caught. Every item below was learned by getting it wrong first, and most of them cost a rewrite.

If you are changing anything about how an effect is drawn — the engine, the shim, the simulation, the preview — read this before you spend an evening on a number you cannot reproduce.

## The tools

`tools/wled.cs` drives a controller over plain HTTP and captures what the strip actually shows, by reading it back over WLED's live preview socket. That socket is the one thing `curl` cannot do, which is why the tool exists. It prints one hex line per frame, ready to check in as a fixture.

```bash
dotnet run tools/wled.cs -- state <address>
dotnet run tools/wled.cs -- capture <address> seg=2 from=25 to=309 fx=73 sx=0 ix=128 pal=11 ms=12000
dotnet run tools/wled.cs -- off <address>
```

`native/against-house-all.py` captures every effect twice and compares both runs against the engine.

**Capture switches every other segment off**, deliberately, so that what is measured is the one segment you asked about and nothing else. That makes it the wrong tool for any question about what a *second* segment is doing while the first one changes — it will read black, and the black is the tool's doing rather than the controller's. For those questions, read `/json/state` and look at whether the segment is still in the table and still on. A segment that exists and is on is being rendered.

## Before you blame the engine

**Ask whether the house agrees with itself.** Many effects accumulate state once per rendered frame from a beat function of the controller's absolute clock, which has been running for days. Colorwaves adds `duration * beatsin88_t(400,5,9)` to its hue on every frame. Two captures of such an effect, minutes apart, differ from each other — so nothing can match it from a single capture, the controller included. Capturing twice is cheap and turns "the engine is wrong" into "nothing could have been right".

**A batch sweep is not N individual captures.** Colorwaves measures 1.33 against the engine when captured on its own, and 30.06 when captured inside a 187-effect sweep. Sweeps of one and three effects are fine. Not the controller slowing down, not the previous effect's state, not the clock origin — all three ruled out by measurement, and the cause is still unknown. Re-capture anything that fails in a sweep before believing it.

**When the engine looks wrong, suspect `native/shim/` first.** Every bug found in it so far has been there rather than in the vendored WLED source or in the architecture:

- `map` written as a macro, so an unsigned subtraction underflowed
- `pgm_read_dword` truncating a 64-bit pointer
- `fastled_config.h` never included, so three behaviour switches were silently off
- no `ARDUINO_ARCH_ESP32`, so `MIN_FRAME_DELAY` fell through to the 8266's 8 instead of the ESP32's 2

## Reading a measurement

**Brightness is usually the wrong signal.** An effect that blends one colour into another is brightest at *both* ends of its swing, so `max(r, g, b)` reports twice as many bands as there are and half the real period. Read the channel the effect actually drives.

**A zero is often the sample, not the effect.** Two assertions here have pinned a zero that turned out to be 170 frames of luck.

**One capture of a random effect is not evidence.** Aurora's mean brightness came out 48.19, 46.75 and 50.23 over three captures, against a port that ranges 42 to 55 across seeds — so a single capture against a single seed can read as a fourteen percent error in either direction. Worse, its first capture had no saturated pixels at all and nearly went into a test as "never saturates". The second had 0.62%.

## Things about WLED that are easy to get wrong

**There are three frame times and they are all different.** The interval between frames (9 ms on the development hardware), WLED's `FRAMETIME` macro (2 ms, from the configured rate), and `FRAMETIME_FIXED` (23 ms, a compile-time constant that Candle asks for). Using one where another belongs is a silent four-fold error.

**Match effects by name, never by number.** WLED's effect ids move between releases, and two of them sit nowhere near where the source order suggests.

**The clock an effect sees restarts when the effect does**, which is why every simulation here starts at zero. Noise Pal proved it: it waits one full change interval before inventing its first palette, so a capture with no settling sits black for exactly that 5.28 seconds before lighting.

**`aux0` and `aux1` are sixteen bits wide; `step` is thirty-two.** The width is behaviour, not storage: Pacifica adds tens of thousands to its counters every frame and needs them to wrap at 65536 to stay periodic.

## Writing the test afterwards

**Never write a test around something being absent.** Twice a test here used an unported effect as its example and broke the day that effect was ported — and the second time, the comment on the test already warned about the first. The engine now offers every effect that is not matrix-only, so there is nothing absent left to lean on.

Measured figures are evidence. They belong in a test or in `native/README.md`, not in a commit message that scrolls away.

## Being a good neighbour about it

These lights are on the outside of somebody's house. Two habits worth keeping:

- **Test in daylight where you can.** Running patterns is far less noticeable to passers-by at two in the afternoon than at ten at night.
- **Leave the lights off when you finish.** `tools/wled.cs` does this itself at the end of a capture, and `off` does it on demand. If you drive a controller some other way, do it yourself.
