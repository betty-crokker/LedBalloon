"""Cuts the airship out of Gemini's mock-up sheet and builds the app icon from it.

The sheet is a picture OF an icon set, not an icon set: every tile is a full-size render with a
label drawn under it, sitting on a white card that is itself drop-shadowed onto a near-white page.
So the artwork has to be lifted off the card before it can be an icon at all - and the shadow is
dark enough to be taken for artwork, which is what defeats a plain flood fill from the corners.
Hence: clear the near-white by reaching in from the border, then keep the largest thing left, which
is the airship and not the arc of shadow under the card.
"""
from collections import deque
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "art/airship-icon-sheet.jpg"

# The big tile only. The small one sits to the right, and the labels are dark text that would
# otherwise count as artwork.
im = Image.open(SRC).convert("RGB").crop((360, 100, 1060, 740))
W, H = im.size
px = im.load()

ink = bytearray(W * H)  # 1 where this is not page or card
for y in range(H):
    for x in range(W):
        r, g, b = px[x, y]
        ink[y * W + x] = 0 if min(r, g, b) >= 200 else 1

# Reach in from the border through the near-white, so white trapped inside the hull stays.
seen = bytearray(W * H)
q = deque()
for x in range(W):
    for y in (0, H - 1):
        if not ink[y * W + x]:
            seen[y * W + x] = 1
            q.append((x, y))
for y in range(H):
    for x in (0, W - 1):
        if not ink[y * W + x]:
            seen[y * W + x] = 1
            q.append((x, y))
while q:
    x, y = q.popleft()
    for nx, ny in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
        if 0 <= nx < W and 0 <= ny < H:
            i = ny * W + nx
            if not seen[i] and not ink[i]:
                seen[i] = 1
                q.append((nx, ny))

# Largest remaining island. The card's shadow is a separate, thinner one.
label = [0] * (W * H)
best, best_n = None, 0
mark = 0
for s in range(W * H):
    if ink[s] and not label[s]:
        mark += 1
        n = 0
        q = deque([s])
        label[s] = mark
        while q:
            i = q.popleft()
            n += 1
            x, y = i % W, i // W
            for nx, ny in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
                if 0 <= nx < W and 0 <= ny < H:
                    j = ny * W + nx
                    if ink[j] and not label[j]:
                        label[j] = mark
                        q.append(j)
        if n > best_n:
            best, best_n = mark, n
print("airship is", best_n, "pixels of", W * H)

out = Image.new("RGBA", (W, H), (0, 0, 0, 0))
op = out.load()
for y in range(H):
    for x in range(W):
        i = y * W + x
        # The hull itself, plus any white it encloses - which the border reach never touched.
        if label[i] == best or (not ink[i] and not seen[i]):
            r, g, b = px[x, y]
            op[x, y] = (r, g, b, 255)

out = out.crop(out.getbbox())
print("cut out to", out.size)

side = max(out.size)
canvas = round(side * 1.06)
square = Image.new("RGBA", (canvas, canvas), (0, 0, 0, 0))
square.paste(out, ((canvas - out.width) // 2, (canvas - out.height) // 2), out)

master = square.resize((512, 512), Image.LANCZOS)

# Windows picks the size it wants out of the file, so all of them go in: 16 for the title bar,
# 24 and 32 for the taskbar, 48 and 64 for shortcuts, 256 for the large view in Explorer.
sizes = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
master.save(ROOT / "src/LedBalloon.App/Assets/ledballoon.ico", sizes=sizes)
master.resize((256, 256), Image.LANCZOS).save(
    ROOT / "src/LedBalloon.App/Assets/ledballoon.png")
print("wrote ledballoon.ico and ledballoon.png")
