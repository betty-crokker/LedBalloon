# native — WLED's effect engine, off the controller

This builds WLED 0.15.3's own effect engine into a DLL that LedBalloon can call, so an effect can be
rendered on a desktop without a controller, a network or a strip.

It exists because the alternative is transliterating C++ into C#, and 118 effects of that produced
ports that were mostly right and occasionally wrong in ways that took a house and a camera to find.
Nothing in `vendor/` has been read, understood or retyped. It is compiled.

## What is whose

| | lines | |
|---|---|---|
| `vendor/wled/` | 15,178 | WLED 0.15.3, byte-identical to upstream |
| `vendor/fastled/` | 8,569 | FastLED 3.7.0, byte-identical to upstream |
| `shim/`, `host.cpp`, `beats.cpp` | 364 | this project's |

That ratio is the whole argument. The "hardware coupling" the effects were supposed to have comes to
an array where the LED bus was and a clock that is told the time rather than reading it.

`bash verify-vendor.sh` checks every vendored file against the upstream tag it claims to come from
and prints a SHA-256 for each. It needs `gh` authenticated and nothing else. If that script passes,
the claim above is true; if it fails, this README is lying and the script is right.

`beats.cpp` is the one piece of hand-cut WLED code: three functions (`beatsin88_t`, `beatsin16_t`,
`beatsin8_t`) lifted out of `util.cpp`, which is vendored but not compiled — the rest of it has 76
errors of its own and is not effect code. The file names its source lines so the extraction can be
checked by eye.

## Building

```bash
pwsh native/build.ps1
```

Produces `native/wledfx.dll`, about 956 KB, depending only on `KERNEL32` and `msvcrt` — both of
which ship with Windows — so it can travel beside the app as one file. The DLL is not checked in.

Needs g++ from MSYS2 (`mingw-w64-x86_64-gcc`). **MSVC cannot build this**: `bus_manager.h` uses
GCC's named-variadic macro extension, `#define DEBUGBUS_PRINTF(x...)`, which is not standard C++ and
has no `/Zc` switch to accept it. That is a property of the vendored source, not a choice — and
switching compilers was preferable to patching a file whose being unpatched is the point.

## How the app uses it

`EffectLibrary` hands out `NativeEffect` instead of this project's ported effects wherever the
engine offers one, so everything downstream - the thumbnails, the strip preview, the house canvas -
draws with the firmware's own code without knowing it changed. 143 effects instead of the ports'
43. `EffectLibrary.UsingEngine` says whether that happened and `EngineUnavailable` says why not;
the fallback is deliberate, so that a build without the engine beside it still runs.

The engine has one segment and the app has a preview per effect, so nothing is left in the engine
between calls: each render hands in that preview's runtime - `aux0`, `aux1`, `step`, `call`, WLED's
per-segment data buffer, and the previous frame, which anything that fades reads back - and takes it
all out again afterwards. `EffectSegment` already carried most of that.

Two things about that are worth knowing before changing it:

- **Do not resize the segment with `setGeometry`.** It marks the segment for reset, so a caller of a
  different length would wipe the state of whichever caller went before it - every frame, with
  previews of different sizes on screen. `wled_length` sets `start` and `stop` directly instead.
  Getting this wrong cost nine passing tests, and `NativeEngineTests` now pins it.
- **`service()` declines to draw twice in the same millisecond**, and the counter it measures
  against is private. Callers stepping in lockstep - which a screenful of previews does - would
  starve each other, so a declined frame is retried three milliseconds later. One of two colliding
  callers is therefore up to three milliseconds late, which is a fifth of a frame.

Publishing needs `-p:IncludeAllContentForSelfExtract=true`, which `publish.ps1` passes. The engine
travels as content rather than as a runtime pack's native library, so without it the engine is
inside the single-file exe but not on disk where the app looks, and the fallback is silent.

## Running it

```bash
dotnet run tools/wledfx.cs -- list
dotnet run tools/wledfx.cs -- render fx=Colorwaves pal=11
dotnet run tools/wledfx.cs -- sweep
```

`render` prints hex RGB triples in the same format `tools/wled.cs capture` prints, so a render here
and a capture off the house go through the same comparison unchanged.

## Four things that cost a day

All four were silent: the thing built, linked, ran, exited zero, and was wrong.

**`map` has to be a function taking `long`, not a macro.** Arduino's is
`long map(long, long, long, long, long)`, and the conversion to `long` is load-bearing. As a macro
each argument keeps its own type, and WLED's callers pass unsigned ones: `police_base` asks for
`map(speed, 0, 255, delay<<4, delay)` with `delay` unsigned, so `out_max - out_min` was `1 - 16`
evaluated unsigned, the division underflowed to nothing, and Two Dots sat at pixel 0 forever. It is
used 77 times in FX.cpp. Found by elimination: `WLEDFX_CLOCK=1` showed the clock the effects see was
advancing perfectly, which ruled out the obvious suspect and left the arithmetic.

**`finalizeInit()` has to run.** `WS2812FX::_length` is private and only `finalizeInit` sets it, and
`WS2812FX::setPixelColor` drops every index at or past it. Skipping it — which looked reasonable,
since it walks bus configuration asking questions about a chip that is not here — silently limits
output to `DEFAULT_LED_COUNT`, which is 30. The symptom was 30 lit pixels out of 285 and a mean
brightness of 11.8 against the house's 116.7. Seed the global `busConfigs` before calling it, or it
falls into its default-bus block hunting for usable GPIO pins and finds none.

**`pgm_read_dword` truncates a pointer on a 64-bit host.** `FX_fcn.cpp:258` reads a pointer *out of*
a PROGMEM table with it, then casts it straight back to `byte*`. On AVR and ESP that pointer is 32
bits wide; here it is 64, so a `uint32_t` read loses half the address. Defined as `uintptr_t` in
`shim/Arduino.h`. It segfaulted every gradient palette, which meant seven effects whose `loadPalette`
substitutes one on palette 0 — Colorwaves among them. g++ warned about it ("cast to pointer from
integer of different size") and the warning was filed as noise for an afternoon.

**Audio-reactive effects need a non-null `um_data_t`.** Nine slots, read at `FX.cpp:6285`. A
`simulateSound` returning `nullptr` crashes all 24 of them. `host.cpp` fills the slots from numbers
the host sets, which `tools/wledfx.cs` drives from the same synthetic rhythm `LedBalloon.Core` uses.
Neither controller here has a microphone, so this is the only way those effects can be seen at
all — and being a pure function of the clock, it is reproducible in a way a microphone is not.

## Where it stands

- 187 of 187 effects render; no crashes across 2,057 (effect, palette) combinations.
- Colorwaves matches the house to the same error as the best hand-written port: 37 brightness waves
  against 37, depth 152 against 161.
- All 24 audio-reactive effects are dark in silence and respond to sound.
- Against the full-closure C# transliteration of Colorwaves: 1.6/255 mean channel difference at
  aligned phase, against about 40 unaligned. Same waveform, slightly different phase — the engine
  carries its own `timebase` through `service()`, where the port is handed `now` directly.

It reproduces one quirk worth recording, because it settles an argument that reading the source had
got wrong twice. On palette 0, Colorwaves shows a **single hue** — the green colour slot — because
`color_from_palette` tests the segment's `palette` field and returns the colour slot before
`loadPalette`'s per-effect substitution can matter. So the substitution is real and unreachable. The
DLL gives hue 120.0° with a spread of 0.0, matching the house; on palette 26, the one `loadPalette`
would substitute, the spread is 121.2°.

## Checked against the house

All 187 effects have been captured off south and compared against the engine, with both sides told
the same effect, palette, speed, intensity, colours, six controls and wiring, and each effect given
its own fxdata defaults. `native/against-house-all.py` runs it; `tools/wled.cs -- sweep` and
`tools/wledfx.cs -- dump` do the two batch halves.

**Of the 57 effects the house reproduces well enough to test, the engine agrees on 55.** Agreement is
usually exact: residuals of 0.00 out of 255 are common.

The two that do not agree are **Scanner** and **Scanner Dual** (`mode_larson_scanner`). At identical
settings - verified by reading the segment back off the controller - the house lights 216 pixels and
the engine at most 94. That effect fades once per rendered frame and advances
`SEGLEN / (FRAMETIME * map(speed,0,255,96,2))` pixels per frame, so it is directly sensitive to the
render rate, but no step from 2 ms to 16 ms reproduces the house's trail. Unexplained, and written
down as such.

### Ask whether the house agrees with itself first

Run the capture sweep twice and compare the two runs before comparing either against the engine. Many
WLED effects are not pure functions of elapsed time and pixel index: they accumulate state once per
rendered frame, and what they accumulate often comes from a beat function of the controller's
absolute clock, which has been running for days. Colorwaves does

```c
sHue16 += duration * beatsin88_t(400, 5, 9);   // 38-second period, read off the absolute clock
```

so two captures of it minutes apart differ in hue, and no spatial or temporal alignment brings them
together. Nothing can match such an effect from a single capture - the controller included. Of the
187: 23 are seeded by `random()`, 9 more fail to reproduce themselves for this reason, 6 are
audio-reactive and the house has no microphone, and 23 are dark on both sides, which agrees.

69 hold too still over a 1.2-second capture to resolve anything. That is the capture length, not the
effects; a longer `ms=` would settle them.

### A batch sweep is not 187 individual measurements

This cost real time, so it is worth stating plainly. Captured on its own, Colorwaves matches the
engine at **1.33**. The same effect captured inside the 187-effect sweep measures **30.06** against
the same engine render. Sweeps of 1 and 3 effects are fine (1.33, 2.72). Ruled out by measurement:
the controller slowing down (frames per capture were flat, 19.6 to 20.6 across the whole run), the
previous effect's state (applying Colorwaves after another effect gives 4.33, from lights-off 5.37),
and the clock origin (swept over a full 38-second beat period; best 24.06 against 35.45).

So the sweep's own tallies are pessimistic. Twelve of the fourteen it called failures agree when
captured individually - Theater Rainbow 10.40 to 0.00, Noise 4 84.00 to 1.41, Twinklefox 42.40 to
0.16, Twinklecat 39.20 to 0.03, Twinkleup 14.30 to 0.00 - which is where the 55 comes from. The
individual numbers are the trustworthy ones.

Not yet done: the app still uses its own ported effects.
