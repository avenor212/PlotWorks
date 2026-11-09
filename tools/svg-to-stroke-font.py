#!/usr/bin/env python3
"""Convert a hand-drawn SVG into a PolyCut single-stroke font (JSON).

Typical workflow:
  1. Draw each character with a pen/line tool in Inkscape - one open path per
     stroke, no fill, no stroke width needed.
  2. Put the character itself in the path id:  <path id="A" d="M ..."/>
     (a Chinese character works too:  <path id="张" d="M ..."/>)
  3. In Inkscape: Path -> Object to Path, then Extension -> Modify Path ->
     Flatten Beziers (tolerance ~1) so every segment is a straight line.
  4. Save as "Plain SVG".
  5. Run:  python tools/svg-to-stroke-font.py my-hand.svg my-hand.json --name "我的手写体"

What it reads:
  * <path id="...">  - id is the character (one character only)
  * coordinates in the SVG user space; y is flipped so the table is y-up with
    the baseline at 0 (the convention HersheyTextRenderer expects)

What it cannot do:
  * curves - flatten them first (see step 3). Curved commands are approximated
    by their control points, which looks wrong, so the script warns loudly.
  * strokes hidden inside <g> groups with a transform - apply transforms first
    (Inkscape: Object -> Transform -> Apply).

Output format matches PolyCut.Shared/Resources/hanzi-strokes.json:
  { "name", "unitsPerEm", "baseline", "left", "right", "glyphs": { "65": {"strokes": [[[x,y],...]]} } }
"""
import argparse
import json
import re
import sys
import xml.etree.ElementTree as ET

SVG_NS = "{http://www.w3.org/2000/svg}"
TOKEN_RE = re.compile(r"([MmLlHhVvCcSsQqTtZz])|(-?\d*\.?\d+(?:[eE][-+]?\d+)?)")


def flatten_path(d, tolerance=2.0):
    """Very small SVG path reader. Straight segments are exact; curves are
    sampled so the result is at least usable. Returns a list of polylines."""
    tokens = []
    for m in TOKEN_RE.finditer(d):
        tokens.append(m.group(0))

    polylines = []
    current = []
    x = y = 0.0
    start_x = start_y = 0.0
    i = 0

    def num():
        nonlocal i
        v = float(tokens[i])
        i += 1
        return v

    def flush():
        nonlocal current
        if len(current) >= 2:
            polylines.append(current)
        current = []

    while i < len(tokens):
        cmd = tokens[i]
        i += 1
        if cmd in "MmLlHhVvZz" or cmd in "CcSsQqTt":
            pass
        else:
            continue

        if cmd in "Zz":
            flush()
            x, y = start_x, start_y
            continue

        relative = cmd.islower()
        kind = cmd.upper()

        if kind == "M":
            flush()
            nx, ny = num(), num()
            if relative:
                nx, ny = x + nx, y + ny
            x, y = nx, ny
            start_x, start_y = x, y
            current = [(x, y)]
            # Subsequent pairs are implicit L
            while i < len(tokens) and tokens[i] not in "MmLlHhVvCcSsQqTtZz":
                nx, ny = num(), num()
                if relative:
                    nx, ny = x + nx, y + ny
                x, y = nx, ny
                current.append((x, y))

        elif kind == "L":
            while i < len(tokens) and tokens[i] not in "MmLlHhVvCcSsQqTtZz":
                nx, ny = num(), num()
                if relative:
                    nx, ny = x + nx, y + ny
                x, y = nx, ny
                current.append((x, y))

        elif kind == "H":
            while i < len(tokens) and tokens[i] not in "MmLlHhVvCcSsQqTtZz":
                nx = num()
                if relative:
                    nx = x + nx
                x = nx
                current.append((x, y))

        elif kind == "V":
            while i < len(tokens) and tokens[i] not in "MmLlHhVvCcSsQqTtZz":
                ny = num()
                if relative:
                    ny = y + ny
                y = ny
                current.append((x, y))

        elif kind == "C":
            while i < len(tokens) and tokens[i] not in "MmLlHhVvCcSsQqTtZz":
                c1x, c1y = num(), num()
                c2x, c2y = num(), num()
                ex, ey = num(), num()
                if relative:
                    c1x, c1y = x + c1x, y + c1y
                    c2x, c2y = x + c2x, y + c2y
                    ex, ey = x + ex, y + ey
                steps = 8
                for s in range(1, steps + 1):
                    t = s / steps
                    mt = 1 - t
                    px = mt**3 * x + 3 * mt**2 * t * c1x + 3 * mt * t**2 * c2x + t**3 * ex
                    py = mt**3 * y + 3 * mt**2 * t * c1y + 3 * mt * t**2 * c2y + t**3 * ey
                    current.append((px, py))
                x, y = ex, ey

        elif kind == "Q":
            while i < len(tokens) and tokens[i] not in "MmLlHhVvCcSsQqTtZz":
                cx, cy = num(), num()
                ex, ey = num(), num()
                if relative:
                    cx, cy = x + cx, y + cy
                    ex, ey = x + ex, y + ey
                steps = 8
                for s in range(1, steps + 1):
                    t = s / steps
                    mt = 1 - t
                    px = mt**2 * x + 2 * mt * t * cx + t**2 * ex
                    py = mt**2 * y + 2 * mt * t * cy + t**2 * ey
                    current.append((px, py))
                x, y = ex, ey

        else:
            # S/T: treat the control point as the current point (crude but safe)
            while i < len(tokens) and tokens[i] not in "MmLlHhVvCcSsQqTtZz":
                num(); num(); num(); num()

    flush()
    return polylines


def rdp(points, eps):
    if len(points) < 3:
        return points
    (x1, y1), (x2, y2) = points[0], points[-1]
    dx, dy = x2 - x1, y2 - y1
    norm = (dx * dx + dy * dy) ** 0.5
    best_i, best_d = 0, -1.0
    for i in range(1, len(points) - 1):
        px, py = points[i]
        d = (((px - x1) ** 2 + (py - y1) ** 2) ** 0.5 if norm == 0
             else abs(dy * px - dx * py + x2 * y1 - y2 * x1) / norm)
        if d > best_d:
            best_i, best_d = i, d
    if best_d <= eps:
        return [points[0], points[-1]]
    left = rdp(points[:best_i + 1], eps)
    right = rdp(points[best_i:], eps)
    return left[:-1] + right


def main():
    ap = argparse.ArgumentParser(description="SVG -> PolyCut single-stroke font")
    ap.add_argument("svg")
    ap.add_argument("out")
    ap.add_argument("--name", default="My Handwriting")
    ap.add_argument("--baseline", type=float, default=None,
                    help="svg y coordinate of the baseline (default: bottom of all paths)")
    ap.add_argument("--flip", action="store_true",
                    help="keep svg y as-is (for tools that already draw y-up)")
    ap.add_argument("--simplify", type=float, default=1.0,
                    help="RDP tolerance in svg units, 0 to disable")
    args = ap.parse_args()

    tree = ET.parse(args.svg)
    root = tree.getroot()

    paths = []
    for el in root.iter():
        tag = el.tag.replace(SVG_NS, "")
        if tag != "path":
            continue
        cid = el.get("id")
        d = el.get("d")
        if not cid or not d:
            continue
        # One character can need several paths (one per stroke), and SVG ids must be
        # unique - so the first character of the id is the glyph, the rest is a label:
        # "A", "A-1", "A-stroke2" all belong to 'A'.
        glyph_char = cid[0]
        paths.append((glyph_char, d))

    if not paths:
        print("No <path id=\"X\"> elements with data found.", file=sys.stderr)
        sys.exit(1)

    raw = {}
    all_y = []
    for cid, d in paths:
        polys = flatten_path(d)
        if not polys:
            continue
        # Several paths can share one glyph (one path per stroke) - accumulate.
        raw.setdefault(cid, []).extend(polys)
        for poly in polys:
            all_y.extend(p[1] for p in poly)

    if not raw:
        print("No usable paths.", file=sys.stderr)
        sys.exit(1)

    baseline = args.baseline if args.baseline is not None else max(all_y)

    glyphs = {}
    total = 0
    for cid, polys in raw.items():
        strokes = []
        for poly in polys:
            pts = [(x, baseline - y if not args.flip else -y) for x, y in poly]
            if args.simplify > 0:
                pts = rdp(pts, args.simplify)
            if len(pts) < 2:
                continue
            strokes.append([[round(x, 1), round(y, 1)] for x, y in pts])
            total += len(pts)
        if strokes:
            glyphs[str(ord(cid))] = {"strokes": strokes}

    doc = {
        "name": args.name,
        "unitsPerEm": 1024,
        "baseline": 0,
        "left": 0,
        "right": 1024,
        "glyphs": glyphs,
    }

    with open(args.out, "w", encoding="utf-8") as fh:
        json.dump(doc, fh, ensure_ascii=False, separators=(",", ":"))

    print(f"wrote {args.out}")
    print(f"  characters: {len(glyphs)}")
    print(f"  points:     {total}")
    print(f"  baseline (svg y): {baseline}")


if __name__ == "__main__":
    main()
