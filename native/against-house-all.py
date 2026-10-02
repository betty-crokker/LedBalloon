"""Compares all 187 of WLED's effects, run through native/wledfx.dll, against the same effects on
the house.

Two batch steps feed it, because one process per effect turns minutes into an hour:

    pwsh native/build.ps1
    WLEDFX_DUMP_INTO=eng dotnet run tools/wledfx.cs -- dump \
        pal=11 sx=128 ix=128 rev=1 ms=11.0 fps=0 settle=0 frames=500 > eng-settings.txt
    WLED_SWEEP_EFFECTS="$(build from eng-settings.txt)" WLED_SWEEP_INTO=house \
        dotnet run tools/wled.cs -- sweep 192.0.2.12 seg=2 from=25 to=309 ms=1200 settle=1000

    python native/against-house-all.py eng house eng-settings.txt

Both sides are given each effect's own fxdata defaults, because WLED's UI applies them when you pick
an effect and the JSON API does not.

Three classes of effect cannot be judged by comparing against one capture, and they are reported
separately rather than counted as failures:

  - audio-reactive ones, because neither controller here has a microphone, so the house shows
    nothing while the engine can be fed a synthetic rhythm;
  - ones seeded with random(), which cannot agree with another machine's generator - and a single
    capture of one is worth nothing either way;
  - ones whose state is a running integral over the controller's own render instants, which are
    irregular. Those do not reproduce themselves, so nothing can match them.
"""
import sys, os, re, statistics
import numpy as np

ENG, HOUSE, SETTINGS = sys.argv[1], sys.argv[2], sys.argv[3]
FX_CPP = os.path.join(os.path.dirname(os.path.abspath(__file__)), "vendor", "wled", "FX.cpp")
SHIFTS = range(-6, 7)


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


def seeded_effects():
    """Effect names that draw on random(), following calls into FX.cpp's helpers.

    Transitively, because the seeding is often one call away: Wipe Random reaches it through
    color_wipe, Ripple through ripple_base. An earlier version looked only for `random...(` in the
    effect's own body and in helpers named *_base, and missed both - it also missed
    get_random_wheel_index, where "random" is not at a word boundary.
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
        if fn in bodies:
            return bodies[fn]
        i = starts[fn]
        depth, out = 0, []
        for line in src[i:]:
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
        text_fn = body(fn)
        if rand.search(text_fn):
            return True
        for called in set(re.findall(r'(\w+)\s*\(', text_fn)):
            if called in starts and called != fn and uses_random(called, seen):
                return True
        return False

    return {name for name, fn in fn_for.items() if uses_random(fn, set())}


SEEDED = seeded_effects()
# Established by measurement, not by reading: these do not reproduce themselves on the controller.
PATH_DEPENDENT = {"Pacifica"}

rows = []
for line in open(SETTINGS, encoding="utf-8"):
    f = line.rstrip("\n").split("\t")
    if len(f) >= 6:
        rows.append((int(f[0]), f[1]))

print("%-4s %-22s %7s %7s %10s %6s  %s"
      % ("fx", "effect", "fitted", "free", "unrelated", "shift", "verdict"))

tally = {}
for mode, name in rows:
    hp = os.path.join(HOUSE, "%d.txt" % mode)
    ep = os.path.join(ENG, "%d.txt" % mode)
    if not os.path.exists(hp) or not os.path.exists(ep):
        print("%-4d %-22s  not captured" % (mode, name))
        tally["not captured"] = tally.get("not captured", 0) + 1
        continue

    house, eng = frames(hp), frames(ep)
    if house is None or eng is None or len(house) < 3:
        print("%-4d %-22s  too few frames" % (mode, name))
        tally["too few frames"] = tally.get("too few frames", 0) + 1
        continue

    width = min(house.shape[1], eng.shape[1])
    house, eng = house[:, :width], eng[:, :width]
    lit_house = int(house.max())
    lit_eng = int(eng.max())

    if lit_house == 0:
        why = ("audio-reactive: no microphone on this house" if lit_eng > 0
               else "dark on both sides")
        print("%-4d %-22s %7s %7s %10s %6s  %s" % (mode, name, "-", "-", "-", "-", why))
        tally[why] = tally.get(why, 0) + 1
        continue

    target = house[len(house) // 2]
    px = width // 3

    best = (None, None)
    for shift in SHIFTS:
        rolled = np.roll(eng.reshape(len(eng), px, 3), shift, axis=1).reshape(len(eng), width)
        d = np.abs(rolled - target).mean(axis=1)
        j = int(d.argmin())
        if best[0] is None or d[j] < best[0]:
            best = (float(d[j]), shift, j, d)

    fitted, shift, j0, dist = best
    # Every other captured frame gets to pick its own engine frame, which is the right comparison
    # because the live preview socket does not push at a constant rate.
    free = []
    for k in range(0, len(house), max(1, len(house) // 6)):
        rolled = np.roll(eng.reshape(len(eng), px, 3), shift, axis=1).reshape(len(eng), width)
        free.append(float(np.abs(rolled - house[k]).mean(axis=1).min()))
    free = statistics.mean(free)
    baseline = float(np.median(dist))

    if baseline < 4:
        verdict = "too static to tell apart"
    elif name in SEEDED:
        verdict = "seeded by random(): not comparable"
    elif name in PATH_DEPENDENT:
        verdict = "integrates over render timing: not comparable"
    elif free < baseline * 0.3:
        verdict = "matches" + ("" if fitted < baseline * 0.3 else ", on the free fit")
    else:
        verdict = "NO MATCH"

    print("%-4d %-22s %7.2f %7.2f %10.2f %6d  %s"
          % (mode, name, fitted, free, baseline, shift, verdict))
    tally[verdict.split(",")[0]] = tally.get(verdict.split(",")[0], 0) + 1

print()
for why, n in sorted(tally.items(), key=lambda kv: -kv[1]):
    print("  %3d  %s" % (n, why))
