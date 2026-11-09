"""
Convert public-domain Hershey .jhf vector fonts into TrueType fonts.

Hershey glyph data: created by Dr. A. V. Hershey (U.S. National Bureau of
Standards). The .jhf file format was created by James Hurt (Cognition, Inc.).
That distribution permits any use, commercial or otherwise, provided the
acknowledgements are kept (see NOTICE below).

Each glyph is stored as open polylines. TrueType has no notion of open paths,
so every stroke is buffered into a very thin polygon (STROKE_RADIUS font units
on each side). At normal text sizes that is a hairline; the plotter/cutter then
draws one line rather than a filled outline.
"""
import os
import sys

from shapely.geometry import LineString
from shapely.ops import unary_union
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen

OFF = ord('R')
SCALE = 1000.0 / 30.0     # hershey units -> font units (cap height ~700)
BASELINE = 9.0            # hershey y of the baseline
STROKE_RADIUS = 4.0       # font units; total stroke width = 8 units (0.8% em)

NOTICE = (
    "Hershey glyph data created by Dr. A. V. Hershey (U.S. National Bureau of Standards). "
    ".jhf format by James Hurt (Cognition, Inc.). Public domain / permissive redistribution."
)


def parse_jhf(path):
    glyphs = []
    with open(path, 'r', encoding='latin-1') as fh:
        for line in fh:
            line = line.rstrip('\r\n')
            if not line or len(line) < 10:
                continue
            nverts = int(line[6:8])
            left = ord(line[8]) - OFF
            right = ord(line[9]) - OFF
            data = line[10:]
            strokes, cur = [], []
            for i in range(0, len(data) - 1, 2):
                c0, c1 = data[i], data[i + 1]
                if c0 == ' ':
                    if cur:
                        strokes.append(cur)
                        cur = []
                    continue
                cur.append((ord(c0) - OFF, ord(c1) - OFF))
            if cur:
                strokes.append(cur)
            glyphs.append(dict(left=left, right=right, strokes=strokes, nverts=nverts))
    return glyphs


def build_contours(g):
    """Buffer every polyline into a thin polygon, merge, return list of (ring, is_hole)."""
    shapes = []
    for s in g['strokes']:
        if len(s) < 2:
            continue
        pts = [((x - g['left']) * SCALE, (BASELINE - y) * SCALE) for x, y in s]
        try:
            shapes.append(LineString(pts).buffer(STROKE_RADIUS, cap_style=1, join_style=1))
        except Exception:
            continue
    if not shapes:
        return [], 0.0

    merged = unary_union(shapes)
    geoms = list(merged.geoms) if merged.geom_type == 'MultiPolygon' else [merged]
    rings = []
    for p in geoms:
        if p.is_empty or p.exterior is None:
            continue
        rings.append(([(round(x, 2), round(y, 2)) for x, y, *_ in p.exterior.coords], False))
        for hole in p.interiors:
            rings.append(([(round(x, 2), round(y, 2)) for x, y, *_ in hole.coords], True))

    if not rings:
        return [], 0.0
    xs = [pt[0] for ring, _ in rings for pt in ring]
    width = max(xs) - min(xs)
    return rings, width


def contour_area(ring):
    a = 0.0
    for (x1, y1), (x2, y2) in zip(ring, ring[1:]):
        a += x1 * y2 - x2 * y1
    return a / 2.0


def convert(src, out_ttf, family, style, postscript):
    glyphs = parse_jhf(src)
    if len(glyphs) < 95:
        print(f"  WARN: {src} has only {len(glyphs)} glyphs")

    glyph_order = ['.notdef']
    cmap = {}
    tt_glyphs = {}
    metrics = {}

    pen = TTGlyphPen(None)
    tt_glyphs['.notdef'] = pen.glyph()
    metrics['.notdef'] = (500, 0)

    for code in range(32, 127):
        idx = code - 32
        if idx >= len(glyphs):
            break
        g = glyphs[idx]
        name = 'space' if code == 32 else f'uni{code:04X}'

        # Advance: Hershey left/right already include side bearings.
        adv = max(0.0, (g['right'] - g['left']) * SCALE)
        if code == 32:
            adv = max(adv, 250.0)

        rings, ink_width = build_contours(g) if code != 32 else ([], 0.0)
        if not rings:
            pen = TTGlyphPen(None)
            tt_glyphs[name] = pen.glyph()
            metrics[name] = (int(round(max(adv, 250.0))), 0)
        else:
            xs = [pt[0] for ring, _ in rings for pt in ring]
            shift = (adv - ink_width) / 2.0 - min(xs)
            pen = TTGlyphPen(None)
            for ring, is_hole in rings:
                area = contour_area(ring)
                pts = [(x + shift, y) for x, y in ring]
                # TrueType/non-zero: outer contours clockwise (negative area, y-up),
                # holes counter-clockwise (positive area).
                want_negative = not is_hole
                if (area < 0) != want_negative:
                    pts = pts[::-1]
                pen.moveTo(pts[0])
                for pt in pts[1:]:
                    pen.lineTo(pt)
                pen.closePath()
            tt_glyphs[name] = pen.glyph()
            metrics[name] = (int(round(max(adv, ink_width + 50))), 0)

        glyph_order.append(name)
        cmap[code] = name

    fb = FontBuilder(1000, isTTF=True)
    fb.setupGlyphOrder(glyph_order)
    fb.setupCharacterMap(cmap)
    fb.setupGlyf(tt_glyphs)
    fb.setupHorizontalMetrics(metrics)
    fb.setupHorizontalHeader(ascent=1000, descent=-350, lineGap=0)
    fb.setupNameTable({
        "familyName": family,
        "styleName": style,
        "uniqueFontIdentifier": f"{postscript};1.0",
        "fullName": f"{family} {style}".strip(),
        "psName": postscript,
        "version": "Version 1.0",
        "copyright": NOTICE,
        "licenseDescription": "Public domain glyph data; free for commercial use.",
    })
    fb.setupOS2(sTypoAscender=1000, sTypoDescender=-350, usWinAscent=1000, usWinDescent=350,
                fsType=0, achVendID="Poly")
    fb.setupPost()
    fb.save(out_ttf)
    print(f"  -> {os.path.basename(out_ttf)}: {len(glyph_order)-1} glyphs")


JOBS = [
    ('scripts.jhf',  'PolyCut Hershey Script Simplex.ttf', 'PolyCut Hershey Script Simplex', '', 'PolyCutHersheyScriptSimplex'),
    ('scriptc.jhf',  'PolyCut Hershey Script Complex.ttf', 'PolyCut Hershey Script Complex', '', 'PolyCutHersheyScriptComplex'),
    ('cursive.jhf',  'PolyCut Hershey Cursive.ttf',        'PolyCut Hershey Cursive',        '', 'PolyCutHersheyCursive'),
    ('gothgbt.jhf',  'PolyCut Hershey Gothic English.ttf', 'PolyCut Hershey Gothic English', '', 'PolyCutHersheyGothicEnglish'),
    ('futural.jhf',  'PolyCut Hershey Roman Simplex.ttf',  'PolyCut Hershey Roman Simplex',  '', 'PolyCutHersheyRomanSimplex'),
]

if __name__ == '__main__':
    src_dir = sys.argv[1]
    out_dir = sys.argv[2]
    os.makedirs(out_dir, exist_ok=True)
    for src, name, fam, style, ps in JOBS:
        path = os.path.join(src_dir, src)
        if not os.path.exists(path):
            print(f"MISSING {src}")
            continue
        print(f"{src} -> {name}")
        convert(path, os.path.join(out_dir, name), fam, style, ps)
