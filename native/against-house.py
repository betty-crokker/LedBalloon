"""Compares WLED's effect engine, run through native/wledfx.dll, against the same effect on the house.

    python native/against-house.py

Needs the DLL built (pwsh native/build.ps1) and south reachable. It leaves the lights off, because
tools/wled.cs does.

Both sides are told the same effect, palette, speed, intensity and colours. Three things are not
known and are fitted rather than assumed:

  - phase: the controller's clock has been running for days and its timebase is its own, so the
    DLL's frame 0 is not the house's frame 0;
  - orientation: segment 2 is wired from the far end and runs with rev set, so what the live preview
    reports is the reverse of the effect's own pixel order;
  - a small pixel offset, for the same reason.

The fit uses the FIRST captured frame only. The remaining frames are then walked without refitting,
stepping the DLL by the capture interval. The fitted residual says the shape is right; the held
residual says the rate is right. An effect that draws the correct picture at the wrong speed passes
the first and fails the second, and that distinction is the whole point of measuring this way.
"""
import subprocess, statistics, sys

import os
REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
HOST = "192.0.2.12"
# Measured per capture rather than assumed. South runs unlimited, so its rate is whatever the wire
# and the loop allow: about 100 fps idle, 95 to 99 while the live preview is streaming. Assuming 9 ms
# was 14 percent fast, which only showed up on the effects that advance per rendered frame.
HOUSE_MS = 10.4
CAPTURE_MS = 2000


def run(args):
    r = subprocess.run(args, cwd=REPO, capture_output=True, text=True, timeout=300)
    if r.returncode != 0:
        tail = (r.stderr.strip().splitlines() or ["(no stderr)"])[-1]
        raise RuntimeError(" ".join(args[:6]) + " -> " + tail[:120])
    return r.stdout


def frames(text):
    out = []
    for line in text.splitlines():
        line = line.strip()
        if len(line) < 6:
            continue
        b = bytes.fromhex(line)
        out.append([(b[i], b[i + 1], b[i + 2]) for i in range(0, len(b) - 2, 3)])
    return out


def wiring():
    """How segment 2 is actually wired, read off the controller rather than assumed."""
    import json, urllib.request
    with urllib.request.urlopen("http://%s/json/state" % HOST, timeout=6) as r:
        state = json.loads(r.read().decode("utf-8", "replace"))
    seg = [x for x in state["seg"] if x["id"] == 2][0]
    return int(bool(seg["rev"])), int(bool(seg["mi"]))


def house_ms():
    """Asks the controller how fast it is actually rendering, while it is rendering."""
    import json, urllib.request
    try:
        with urllib.request.urlopen("http://%s/json/info" % HOST, timeout=5) as r:
            fps = json.loads(r.read().decode("utf-8", "replace"))["leds"]["fps"]
        return 1000.0 / fps if fps else HOUSE_MS
    except Exception:
        return HOUSE_MS


def capture(fx, chosen):
    args = ["dotnet", "run", "tools/wled.cs", "--", "capture", HOST,
            "seg=2", "from=25", "to=309",
            "col0=00ff00", "col1=000000", "col2=000000",
            f"fx={fx}", f"ms={CAPTURE_MS}", "settle=1500"]
    for key, value in chosen.items():
        # WLED reads o1/o2/o3 through getBoolVal, which ignores a number and keeps the default.
        args.append(f"{key}={'true' if value else 'false'}" if key.startswith("o")
                    else f"{key}={value}")
    return frames(run(args))


def render(name, chosen, count, ms):
    args = ["dotnet", "run", "tools/wledfx.cs", "--", "render", f"fx={name}",
            "col0=00ff00", "col1=000000", "col2=000000",
            f"ms={ms:.3f}", "fps=0", "settle=0", f"frames={count}"]
    args += [f"{key}={value}" for key, value in chosen.items()]
    args += [f"rev={REV}", f"mi={MIR}"]
    return frames(run(args))


def diff(a, b, shift):
    n = len(a)
    picked = range(0, n, 2)                       # every other pixel: same answer, half the work
    total = 0
    for i in picked:
        p, q = a[i], b[(i + shift) % n]
        total += abs(p[0] - q[0]) + abs(p[1] - q[1]) + abs(p[2] - q[2])
    return total / (3 * len(picked))


# Each effect is run with the settings its own fxdata asks for, which is what a controller's UI
# applies when you pick it. Overriding them with one blanket set made Palette render a single colour
# with no movement - its defaults turn Animate Shift on - and a static effect cannot tell agreement
# from coincidence. Section 5 of the fxdata is that default list.
IDS, DEFAULTS = {}, {}
WANTS = ("sx", "ix", "c1", "c2", "c3", "o1", "o2", "o3", "pal")
for line in run(["dotnet", "run", "tools/wledfx.cs", "--", "list"]).splitlines()[1:]:
    if len(line) > 5 and line[:3].strip().isdigit():
        name = line[5:27].strip()
        IDS[name] = int(line[:3])
        parts = line[27:].strip().split(";")
        declared = {}
        if len(parts) > 4:
            for pair in parts[4].split(","):
                if "=" in pair:
                    key, value = pair.split("=", 1)
                    if key.strip() in WANTS and value.strip().lstrip("-").isdigit():
                        declared[key.strip()] = int(value)
        DEFAULTS[name] = declared


def settings(name, pal, sx, ix):
    """What both sides get told: this project's baseline, with the effect's own defaults on top."""
    chosen = {"pal": pal, "sx": sx, "ix": ix,
              "c1": 128, "c2": 128, "c3": 16, "o1": 0, "o2": 0, "o3": 0}
    chosen.update(DEFAULTS.get(name, {}))
    return chosen


REV, MIR = wiring()
print("segment 2 is wired rev=%d mi=%d; the engine is told the same" % (REV, MIR))



def compare(name, pal=11, sx=128, ix=128):
    chosen = settings(name, pal, sx, ix)
    house = capture(IDS[name], chosen)
    if len(house) < 4:
        raise RuntimeError("capture returned %d frames" % len(house))

    # The capture's interval, measured rather than assumed: the live preview socket is throttled and
    # does not push at the rate the strip renders. Kept fractional - rounding it to a whole number of
    # DLL frames drifts by the remainder every frame, which reads as an effect whose motion is wrong
    # when it is the measurement that is wrong.
    ms = house_ms()
    step = (CAPTURE_MS / len(house)) / ms
    dll = render(name, chosen, count=900, ms=ms)

    # Orientation is no longer fitted. The engine is told how the segment is wired, so it renders in
    # bus order already - and fitting a reversal that is known lets a near-symmetric frame match
    # itself backwards, which is how Rainbow Runner came to be measured double-reversed.
    best = None
    for j in range(len(dll)):
        for s in range(-4, 5):
            d = diff(house[0], dll[j], s)
            if best is None or d < best[0]:
                best = (d, j, s, False)

    first, j0, shift, flip = best
    want = house

    held = []
    for k in range(1, min(len(house), 12)):
        j = j0 + round(k * step)
        if j >= len(dll):
            break
        held.append(diff(want[k], dll[j], shift))

    # Does each later captured frame have *some* DLL frame that matches it, and do those picks march
    # forward in step? If they do, the shapes are all right and only the assumption that the live
    # preview pushes at a constant rate was wrong - it does not, it pushes when it can.
    free, picks = [], []
    for k in range(1, min(len(house), 12)):
        centre = j0 + round(k * step)
        window = range(max(0, centre - 60), min(len(dll), centre + 61))
        if not window:
            break
        d, j = min(((diff(want[k], dll[j], shift), j) for j in window), key=lambda t: t[0])
        free.append(d)
        picks.append(j)

    marching = (picks[-1] > picks[0]) if len(picks) > 2 else None

    # What a wrong answer looks like, for scale: the same frame against unrelated DLL frames.
    baseline = statistics.median(
        diff(want[0], dll[(j0 + 97 + (31 * i)) % len(dll)], shift) for i in range(9))

    return (first, (statistics.mean(held) if held else None), baseline, flip, shift,
            (statistics.mean(free) if free else None), marching, ms)





WANTED = ["Flow", "Pacifica", "Noise 2", "Colorwaves", "Rainbow", "Two Dots",
          "Lake", "Breathe", "Rainbow Runner", "Palette", "Plasma", "Running"]

print("%-16s %7s %7s %7s %9s %5s %6s  %s"
      % ("effect", "fitted", "held", "free", "unrelated", "flip", "march", "verdict"))
rows = []
for name in WANTED:
    if name not in IDS:
        print("%-16s  not in this engine's table" % name)
        continue
    try:
        first, held, baseline, flip, shift, free, marching, ms = compare(name)
    except Exception as exc:
        print("%-16s  %s" % (name, str(exc).splitlines()[-1][:70]))
        continue
    if held is None:
        print("%-16s %7.2f %7s %10.2f" % (name, first, "-", baseline))
        continue

    # An effect that holds still gives the same residual however it is aligned, so agreement on one
    # carries no information either way. Those are reported, not scored.
    if baseline < 5:
        verdict = "static - nothing to tell apart"
    else:
        shape = first < baseline * 0.3
        motion = held < baseline * 0.3
        tracks = free is not None and free < baseline * 0.3 and marching is not False
        verdict = ("matches" if shape and motion
                   else "matches, preview pacing uneven" if shape and tracks
                   else "shape right, rate wrong" if shape
                   else "no match")
    print("%-16s %7.2f %7.2f %7.2f %9.2f %5s %6s  %s"
          % (name, first, held, free if free is not None else -1, baseline,
             "yes" if flip else "no",
             "yes" if marching else ("no" if marching is False else "-"), verdict))
    rows.append((name, first, held, baseline, verdict))
    sys.stdout.flush()

print()
print("residuals are mean channel difference out of 255; 'unrelated' is the same captured frame")
print("against unrelated DLL frames, so it is what no agreement looks like for that effect.")
if rows:
    print()
    good = sum(1 for r in rows if r[4].startswith("matches"))
    static = sum(1 for r in rows if r[4].startswith("static"))
    print("%d of %d agree with the house; %d right in shape but not in rate; %d no match; "
          "%d too static to tell."
          % (good, len(rows),
             sum(1 for r in rows if r[4] == "shape right, rate wrong"),
             sum(1 for r in rows if r[4] == "no match"), static))
