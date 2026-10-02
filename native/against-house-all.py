"""Compares all 187 of WLED's effects, run through native/wledfx.dll, against the same effects on
the house - and first asks, for each effect, whether the house agrees with *itself*.

That ordering is the whole point. Many WLED effects are not pure functions of elapsed time and pixel
index: they accumulate state once per rendered frame, and the amount they accumulate often comes from
a beat function of the controller's absolute clock, which has been running for days. Colorwaves does

    sHue16 += duration * beatsin88_t(400, 5, 9);

so two captures of it minutes apart differ in hue, and no spatial or temporal alignment can bring
them together. Such effects cannot be validated against a single capture by anything, including the
controller itself. Measuring that first turns "the engine disagrees" into "nothing could agree", and
only the effects that pass it say anything about the engine.

Feed it two independent capture runs and one engine render:

    pwsh native/build.ps1
    WLEDFX_DUMP_INTO=eng dotnet run tools/wledfx.cs -- dump \
        pal=11 sx=128 ix=128 rev=1 ms=11.0 fps=0 settle=0 frames=900 > eng-settings.txt
    WLED_SWEEP_EFFECTS=... WLED_SWEEP_INTO=houseA dotnet run tools/wled.cs -- sweep <host> ...
    WLED_SWEEP_EFFECTS=... WLED_SWEEP_INTO=houseB dotnet run tools/wled.cs -- sweep <host> ...

    python native/against-house-all.py eng houseA houseB eng-settings.txt
"""
import sys, os, re, statistics
import numpy as np

ENG, HOUSE_A, HOUSE_B, SETTINGS = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4]
FX_CPP = os.path.join(os.path.dirname(os.path.abspath(__file__)), "vendor", "wled", "FX.cpp")
SHIFTS = range(-8, 9)


def frames(path):
    rows = []
    for line in open(path):
        line = line.strip()
        if len(line) < 6:
            continue
        rows.append(np.frombuffer(bytes.fromhex(line), dtype=np.uint8).astype(np.int16))
    if not rows:
        return None
    width = min(len(r) for r in rows)
    return np.stack([r[:width] for r in rows])


def closest(target, candidates, width):
    """Smallest mean channel difference between one frame and any of a set, over small shifts."""
    px = width // 3
    best = None
    for shift in SHIFTS:
        rolled = np.roll(candidates[:, :width].reshape(len(candidates), px, 3),
                         shift, axis=1).reshape(len(candidates), width)
        d = np.abs(rolled - target[:width]).mean(axis=1)
        j = int(d.argmin())
        if best is None or d[j] < best[0]:
            best = (float(d[j]), shift, j, d)
    return best


def seeded_effects():
    """Effect names that draw on random(), following calls through FX.cpp's helpers.

    Transitively, because the seeding is often one call away: Wipe Random reaches it through
    color_wipe, Ripple through ripple_base. Also catches get_random_wheel_index, where "random" is
    not at a word boundary.
    """
    text = open(FX_CPP, encoding="utf-8", errors="replace").read()
    src = text.splitlines()
    data = {m.group(1): m.group(2) for m in
            re.finditer(r'(_data_FX_MODE_\w+)\[\]\s*PROGMEM\s*=\s*"([^"@]*)', text)}
    fn_for = {}
    for m in re.finditer(r'addEffect\(\s*\w+\s*,\s*&(mode_\w+)\s*,\s*(_data_FX_MODE_\w+)\s*\)', text):
        if m.group(2) in data:
            fn_for[data[m.group(2)]] = m.group(1)

    starts = {}
    for i, line in enumerate(src):
        m = re.match(r'^\s*(?:static\s+)?(?:uint\d+_t|void|CRGB|int|bool)\s+(\w+)\s*\(', line)
        if m:
            starts.setdefault(m.group(1), i)

    bodies = {}

    def body(fn):
        if fn not in bodies:
            depth, out = 0, []
            for line in src[starts[fn]:]:
                out.append(line)
                depth += line.count("{") - line.count("}")
                if depth == 0 and len(out) > 1:
                    break
            bodies[fn] = "\n".join(out)
        return bodies[fn]

    rand = re.compile(r'random\w*\s*\(|hw_random|rand16seed')

    def uses_random(fn, seen):
        if fn in seen or fn not in starts:
            return False
        seen.add(fn)
        here = body(fn)
        if rand.search(here):
            return True
        return any(uses_random(c, seen) for c in set(re.findall(r'\b(\w+)\s*\(', here))
                   if c in starts and c != fn)

    return {name for name, fn in fn_for.items() if uses_random(fn, set())}


SEEDED = seeded_effects()

rows = []
for line in open(SETTINGS, encoding="utf-8"):
    f = line.rstrip("\n").split("\t")
    if len(f) >= 2:
        rows.append((int(f[0]), f[1]))

print("%-4s %-22s %8s %8s %8s  %s"
      % ("fx", "effect", "motion", "itself", "engine", "verdict"))

tally, detail = {}, []
for mode, name in rows:
    paths = [os.path.join(d, "%d.txt" % mode) for d in (HOUSE_A, HOUSE_B, ENG)]
    if not all(os.path.exists(p) for p in paths):
        print("%-4d %-22s  not captured" % (mode, name))
        tally["not captured"] = tally.get("not captured", 0) + 1
        continue

    a, b, eng = (frames(p) for p in paths)
    if a is None or b is None or eng is None or min(len(a), len(b)) < 6:
        print("%-4d %-22s  too few frames" % (mode, name))
        tally["too few frames"] = tally.get("too few frames", 0) + 1
        continue

    width = min(a.shape[1], b.shape[1], eng.shape[1])
    if int(a.max()) == 0 and int(b.max()) == 0:
        why = ("audio-reactive: the house has no microphone" if int(eng.max()) > 0
               else "dark on both sides, which agrees")
        print("%-4d %-22s %8s %8s %8s  %s" % (mode, name, "-", "-", "-", why))
        tally[why] = tally.get(why, 0) + 1
        continue

    mid = len(a) // 2
    target = a[mid]
    # How much does the effect move over one capture? Nothing can be resolved below that.
    motion = float(np.median([np.abs(target - a[k]).mean()
                              for k in range(len(a)) if abs(k - mid) > 2]))
    itself = closest(target, b, width)[0]            # the house against its own second run
    engine = closest(target, eng, width)[0]          # the house against the engine
    floor = max(2.0, motion * 0.25)

    if motion < 2:
        verdict = "holds still over a capture: nothing to resolve"
    elif itself > floor:
        verdict = ("not reproducible on the hardware: seeded by random()" if name in SEEDED
                   else "not reproducible on the hardware")
    elif engine <= floor:
        verdict = "matches"
    else:
        verdict = "NO MATCH"

    print("%-4d %-22s %8.2f %8.2f %8.2f  %s" % (mode, name, motion, itself, engine, verdict))
    tally[verdict] = tally.get(verdict, 0) + 1
    detail.append((mode, name, motion, itself, engine, verdict))

print()
for why, n in sorted(tally.items(), key=lambda kv: -kv[1]):
    print("  %3d  %s" % (n, why))

testable = [d for d in detail if d[5] in ("matches", "NO MATCH")]
if testable:
    agree = sum(1 for d in testable if d[5] == "matches")
    print()
    print("Of the %d effects the house reproduces well enough to test, the engine agrees on %d."
          % (len(testable), agree))
    bad = [d for d in testable if d[5] == "NO MATCH"]
    if bad:
        print("Not agreeing: " + ", ".join("%s (%.1f vs %.1f reproducible)" % (d[1], d[4], d[3])
                                           for d in bad))
