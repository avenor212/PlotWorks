#!/usr/bin/env python3
"""Build a single-stroke Chinese glyph table from Make Me a Hanzi medians.

Input : tools/hershey-src/graphics.txt  (one JSON object per line, 9574 chars)
Output: PolyCut.Shared/Resources/hanzi-strokes.json

Each character keeps only its "medians" - the hand-written centre line of every
stroke - which is exactly a single-stroke plot. Coordinates are mapped to
1024 units per em with the baseline at y = 0 and y pointing up, matching the
Hershey renderer's convention (see HersheyTextRenderer.ToDevice).

Coverage: the whole GB2312 set (6763 characters, level 1 and level 2) - names
like 珺/玥/睿/曦/宸 live in level 2, and leaving them out used to make whole
characters disappear silently from a name plate. Full-width punctuation is not
in the source data at all, so its stroke skeletons are written out below.

Source data: Make Me a Hanzi (github.com/skishore/makemeahanzi), derived from
Arphic PL KaitiM GB / UKai, Arphic Public License.
"""
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "tools" / "hershey-src" / "graphics.txt"
OUT = ROOT / "PolyCut.Shared" / "Resources" / "hanzi-strokes.json"

UPEM = 1024.0
BASELINE = 900.0   # svg y of the glyph baseline in the 1024 grid
EPSILON = 2.0      # Ramer-Douglas-Peucker tolerance, in 1024-grid units


def circle(cx, cy, r, steps=12):
    """Closed polygon approximating a circle, for 。  Coordinates are in the same
    y-up space as the medians (baseline at 0, glyph top near 700)."""
    import math
    pts = []
    for i in range(steps + 1):
        a = 2 * math.pi * i / steps
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


def heart(cx, cy, size, steps=24):
    """Closed polyline heart for ♥. The classic parametric heart is written in a
    y-up space, which is the space this table uses, so no flip here."""
    import math
    pts = []
    for i in range(steps + 1):
        t = 2 * math.pi * i / steps
        x = 16 * math.sin(t) ** 3
        y = 13 * math.cos(t) - 5 * math.cos(2 * t) - 2 * math.cos(3 * t) - math.cos(4 * t)
        pts.append((cx + x * size / 16.0, cy + y * size / 16.0))
    return pts


# Stroke skeletons for full-width punctuation. Coordinates here are y-UP with the
# baseline at 0; `main()` flips them with BASELINE - y, which lands them in the
# same y-DOWN space as the medians (a Han glyph spans about y = 141..859).
#
# Design targets are therefore written as "900 - (where it should end up)":
# 。 belongs low and left (ends near y ≈ 760), not in the middle of the box -
# the first build put it mid-box and it plotted as if it were top-right.
PUNCTUATION = {
    # Ink is kept inside the LEFT HALF of the em box: a full-width mark still occupies a
    # whole cell, but anything centred (x≈520) or sitting past the midline reads as
    # floating between the two characters instead of attached to the one before it.
    0xFF0C: [[(180, 260), (165, 190), (135, 120)]],                           # ，
    0x3002: [circle(110, 140, 62, 12)],                                       # 。 centred at x=110
    0xFF01: [[(230, 700), (230, 200)], [(230, 140), (230, 175)]],             # ！
    0xFF1F: [[(235, 570), (243, 650), (300, 720), (368, 700), (380, 645),
              (336, 600), (304, 570), (300, 545)],
             [(300, 400), (300, 435)]],                                       # ？
    0x3001: [[(180, 260), (145, 150)]],                                       # 、
    0xFF1B: [[(180, 550), (180, 585)],
             [(180, 260), (165, 190), (135, 120)]],                           # ；
    0xFF1A: [[(230, 470), (230, 505)], [(230, 170), (230, 205)]],             # ：
    0xFF08: [[(600, 780), (560, 690), (545, 520), (560, 350), (600, 260)]],   # （
    0xFF09: [[(440, 780), (480, 690), (495, 520), (480, 350), (440, 260)]],   # ）
    0x300C: [[(600, 720), (545, 720), (545, 140), (600, 140)]],               # 「
    0x300D: [[(440, 720), (495, 720), (495, 140), (440, 140)]],               # 」
    0x300A: [[(590, 780), (520, 600), (520, 400), (590, 220)]],               # 《
    0x300B: [[(450, 780), (520, 600), (520, 400), (450, 220)]],               # 》
    0xFF0E: [circle(520, 120, 48, 8)],                                        # ．
    0x2665: [heart(512, 500, 340, 24)],                                       # ♥
    0x2661: [heart(512, 500, 340, 24)],                                       # ♡
}



def rdp(points, eps):
    """Ramer-Douglas-Peucker polyline simplification."""
    if len(points) < 3:
        return points
    (x1, y1), (x2, y2) = points[0], points[-1]
    dx, dy = x2 - x1, y2 - y1
    norm = (dx * dx + dy * dy) ** 0.5
    best_i, best_d = 0, -1.0
    for i in range(1, len(points) - 1):
        px, py = points[i]
        if norm == 0:
            d = ((px - x1) ** 2 + (py - y1) ** 2) ** 0.5
        else:
            d = abs(dy * px - dx * py + x2 * y1 - y2 * x1) / norm
        if d > best_d:
            best_i, best_d = i, d
    if best_d <= eps:
        return [points[0], points[-1]]
    left = rdp(points[:best_i + 1], eps)
    right = rdp(points[best_i:], eps)
    return left[:-1] + right


def in_gb2312(ch):
    """Level 1 *and* level 2 - the earlier level-1-only filter silently dropped
    characters like 珺 玥 睿 曦 宸 晟 梓 萱 琪 怡 婷 鑫 淼 犇."""
    try:
        ch.encode("gb2312")
    except UnicodeEncodeError:
        return False
    return True


def main():
    if not SRC.exists():
        sys.exit(f"missing {SRC}")

    glyphs = {}
    total_pts = 0

    # Punctuation first: it is not in the medians data at all.
    for code, strokes in PUNCTUATION.items():
        out = []
        for stroke in strokes:
            pts = [(float(x), BASELINE - float(y)) for x, y in stroke]
            out.append([[round(x, 1), round(y, 1)] for x, y in pts])
            total_pts += len(pts)
        if out:
            glyphs[str(code)] = {"strokes": out}

    considered = 0
    with SRC.open(encoding="utf-8") as fh:
        for line in fh:
            line = line.strip()
            if not line:
                continue
            obj = json.loads(line)
            ch = obj["character"]
            if len(ch) != 1:
                continue
            considered += 1
            if not in_gb2312(ch):
                continue
            medians = obj.get("medians") or []
            strokes = []
            for median in medians:
                if len(median) < 2:
                    continue
                pts = [(float(x), BASELINE - float(y)) for x, y in median]
                pts = rdp(pts, EPSILON)
                if len(pts) < 2:
                    continue
                strokes.append([[round(x, 1), round(y, 1)] for x, y in pts])
                total_pts += len(pts)
            if strokes:
                glyphs[str(ord(ch))] = {"strokes": strokes}

    doc = {
        "name": "PolyCut Hanzi Kaiti",
        "unitsPerEm": UPEM,
        "baseline": 0,
        "left": 0,
        "right": UPEM,
        "source": "Make Me a Hanzi medians (Arphic PL KaitiM GB), Arphic Public License",
        "glyphs": glyphs,
    }
    OUT.parent.mkdir(parents=True, exist_ok=True)
    with OUT.open("w", encoding="utf-8") as fh:
        json.dump(doc, fh, ensure_ascii=False, separators=(",", ":"))
    size = OUT.stat().st_size
    print(f"considered={considered} kept={len(glyphs)} points={total_pts} bytes={size}")


if __name__ == "__main__":
    main()
