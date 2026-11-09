"""Build a PolyCut G-code file straight from the stroke tables.

Driving the app through UI automation to produce a small test plate is slow and keeps
tripping over real bugs (Esc deletes a newly created text box). For checking glyph
layout this is both faster and more precise: the geometry comes from the same JSON the
plotter reads, and the start/end blocks match what the app emits.

    python tools/text-to-gcode.py "唱跳 Rap" -o Docs/gcode-tests/mix-test.gcode --size 24
"""

import argparse
import json
import unicodedata
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
RES = ROOT / "PolyCut.Shared" / "Resources"

HANZI_EM = 1024.0
HANZI_BASELINE = 900.0     # bottom of a normal Han glyph inside the 1024 em
BED = 256.0


def load():
    hanzi = json.load(open(RES / "hanzi-strokes.json", encoding="utf-8"))["glyphs"]
    data = json.load(open(RES / "hershey-glyphs.json", encoding="utf-8"))
    font = data["fonts"][0]
    return hanzi, font


def decompose_latin(code):
    """Mirror of TryDecomposeLatin: base letter plus combining marks."""
    if code < 128 or code > 0x24F:
        return None
    d = unicodedata.normalize("NFD", chr(code))
    if len(d) < 2:
        return None
    base = ord(d[0])
    if base >= 128:
        return None
    marks = [ord(c) for c in d[1:]]
    if any(m < 0x300 or m > 0x36F for m in marks):
        return None
    return base, marks


def accent_mark(code, cx, bottom, size):
    """Same shapes as HersheyTextRenderer.AddAccentMark (anchored at the lowest point)."""
    w = size * 0.16
    h = size * 0.11
    y = bottom
    if code == 0x304:                       # macron
        return [[(cx - w, y), (cx + w, y)]]
    if code == 0x301:                       # acute, rises to the right
        return [[(cx - w, y), (cx + w, y - h * 1.6)]]
    if code == 0x300:                       # grave, falls to the right
        return [[(cx - w, y - h * 1.6), (cx + w, y)]]
    if code == 0x30C:                       # caron
        return [[(cx - w, y - h), (cx, y), (cx + w, y - h)]]
    if code == 0x308:                       # diaeresis
        r = 0.6
        return [[(cx - w * 0.5 - r, y), (cx - w * 0.5 + r, y)],
                [(cx + w * 0.5 - r, y), (cx + w * 0.5 + r, y)]]
    return []


def build(text, size):
    """Return (strokes in canvas mm, total advance). Baseline sits at y == size."""
    hanzi, font = load()
    glyphs = font["glyphs"]
    upm = font["unitsPerEm"]
    baseline = font["baseline"]
    latin_scale = size / upm
    hs = size / HANZI_EM

    pen = 0.0
    out = []

    for ch in text:
        code = ord(ch)
        base, marks = code, []
        if code > 127:
            d = decompose_latin(code)
            if d:
                base, marks = d

        if base > 127:
            entry = hanzi.get(str(base))
            if entry is None:
                raise SystemExit(f"no stroke data for {ch!r} (U+{base:04X})")
            for s in entry["strokes"]:
                # Anchor the glyph's bottom (HANZI_BASELINE) on the text baseline, the
                # same line the Latin letters stand on.
                out.append([(pen + p[0] * hs, size + (p[1] - HANZI_BASELINE) * hs) for p in s])
            pen += size
            continue

        g = glyphs.get(str(base))
        if g is None:
            raise SystemExit(f"no latin glyph for {base!r}")
        left = g["left"]
        adv = (g["right"] - g["left"]) * latin_scale
        ink = []
        for s in g["strokes"]:
            pts = [(pen + (p[0] - left) * latin_scale, size + (baseline - p[1]) * latin_scale) for p in s]
            ink.append(pts)
            out.append(pts)

        if marks:
            cx = pen + adv / 2
            top_y = min(p[1] for pts in ink for p in pts)
            bottom = top_y - size * 0.03
            order = marks
            if marks[0] != 0x308 and 0x308 in marks:
                order = [0x308] + [m for m in marks if m != 0x308]
            for k, m in enumerate(order):
                yy = bottom - (0 if k == 0 else size * 0.12)
                out.extend(accent_mark(m, cx, yy, size))

        pen += adv

    return out, pen


START = """;######################################
;Custom Start GCode
; ==== 关掉加热，确认单位 ====
M104 S0
M140 S0
G21

; ==== 开始提示音：机器马上要动了 ====
M17
M400 S1
M1006 S1
M1006 A0 B10 L100 C37 D10 M60 E37 F10 N60
M1006 A0 B10 L100 C41 D10 M60 E41 F10 N60
M1006 A0 B10 L100 C44 D10 M60 E44 F10 N60
M1006 A0 B10 L100 C0 D10 M60 E0 F10 N60
M1006 A43 B10 L100 C46 D10 M70 E39 F10 N80
M1006 A0 B10 L100 C0 D10 M60 E0 F10 N80
M1006 A0 B10 L100 C43 D10 M60 E39 F10 N80
M1006 A0 B10 L100 C0 D10 M60 E0 F10 N80
M1006 A0 B10 L100 C41 D10 M80 E41 F10 N80
M1006 A0 B10 L100 C44 D10 M80 E44 F10 N80
M1006 A0 B10 L100 C49 D10 M80 E49 F10 N80
M1006 A0 B10 L100 C0 D10 M80 E0 F10 N80
M1006 A44 B10 L100 C48 D10 M60 E39 F10 N80
M1006 A0 B10 L100 C0 D10 M60 E0 F10 N80
M1006 A0 B10 L100 C44 D10 M80 E39 F10 N80
M1006 A0 B10 L100 C0 D10 M60 E0 F10 N80
M1006 A43 B10 L100 C46 D10 M60 E39 F10 N80
M1006 W

; ==== 回零 ====
G28
"""

END = """;######################################
;Custom End GCode
; ==== 结束：抬笔、退到取纸位置 ====
G91
G1 Z10 F600
G90
G1 X10 Y251 F3000
M400
M18 X Y
"""


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("text")
    ap.add_argument("-o", "--out", required=True)
    ap.add_argument("--size", type=float, default=24.0, help="font size in mm (24 mm = 68 pt)")
    ap.add_argument("--origin-x", type=float, default=40.0)
    ap.add_argument("--origin-y", type=float, default=40.0, help="canvas Y of the baseline")
    ap.add_argument("--work-z", type=float, default=5.0)
    ap.add_argument("--travel-z", type=float, default=10.0)
    args = ap.parse_args()

    strokes, width = build(args.text, args.size)
    ox, oy = args.origin_x, args.origin_y

    def to_machine(p):
        return ox + p[0], BED - (oy + p[1])

    lines = []
    lines.append("; PolyCut layout test - generated by tools/text-to-gcode.py")
    lines.append(f"; text: {args.text}")
    lines.append(f"; size: {args.size} mm   canvas X {ox:.1f}..{ox + width:.1f}")
    lines.append("")
    lines.append(START)

    n = 0
    for stroke in strokes:
        if len(stroke) < 2:
            continue
        mx, my = to_machine(stroke[0])
        lines.append(f"G0 X{mx:.3f} Y{my:.3f} Z{args.travel_z} F3600")
        lines.append(f"G0 Z{args.work_z}")
        for p in stroke[1:]:
            mx, my = to_machine(p)
            lines.append(f"G1 X{mx:.3f} Y{my:.3f} F2400")
        lines.append(f"G0 Z{args.travel_z} F600")
        n += 1

    lines.append("")
    lines.append(END)
    lines.append(f"; strokes = {n}")

    Path(args.out).write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"wrote {args.out}")
    print(f"  strokes: {n}   advance: {width:.1f} mm")


if __name__ == "__main__":
    main()
