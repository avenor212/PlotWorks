"""Export Hershey .jhf glyphs to the JSON table embedded in PolyCut.

The app draws text with these fonts as open polylines instead of font outlines, so one
pen stroke plots as one path rather than a thin closed band (which a plotter would trace
twice). Coordinates stay in Hershey units; the renderer scales by fontSize / 30.
"""
import json
import os
import sys

OFF = ord('R')
BASELINE = 9          # hershey y of the baseline
UNITS_PER_EM = 30.0   # 30 hershey units == 1 em

SOURCES = [
    ("scripts.jhf", "PolyCut Hershey Script Simplex"),
    ("scriptc.jhf", "PolyCut Hershey Script Complex"),
    ("cursive.jhf", "PolyCut Hershey Cursive"),
    ("gothgbt.jhf", "PolyCut Hershey Gothic English"),
    ("futural.jhf", "PolyCut Hershey Roman Simplex"),
]


def parse_jhf(path):
    glyphs = {}
    with open(path, 'r', encoding='latin-1') as fh:
        index = 0
        for line in fh:
            line = line.rstrip('\r\n')
            if not line or len(line) < 10:
                continue
            code = 32 + index
            index += 1
            if code > 126:
                break

            left = ord(line[8]) - OFF
            right = ord(line[9]) - OFF
            data = line[10:]

            strokes, cur = [], []
            for i in range(0, len(data) - 1, 2):
                c0, c1 = data[i], data[i + 1]
                if c0 == ' ':
                    if len(cur) >= 2:
                        strokes.append(cur)
                    cur = []
                    continue
                cur.append([ord(c0) - OFF, ord(c1) - OFF])
            if len(cur) >= 2:
                strokes.append(cur)

            glyphs[str(code)] = {
                "left": left,
                "right": right,
                "strokes": strokes,
            }
    return glyphs


def main(src_dir, out_path):
    fonts = []
    for filename, family in SOURCES:
        path = os.path.join(src_dir, filename)
        if not os.path.exists(path):
            print(f"MISSING {filename}")
            continue
        glyphs = parse_jhf(path)
        fonts.append({
            "name": family,
            "baseline": BASELINE,
            "unitsPerEm": UNITS_PER_EM,
            "glyphs": glyphs,
        })
        print(f"{family}: {len(glyphs)} glyphs")

    payload = {"fonts": fonts}
    with open(out_path, 'w', encoding='utf-8') as fh:
        json.dump(payload, fh, separators=(',', ':'), ensure_ascii=False)
    print(f"wrote {out_path} ({os.path.getsize(out_path)} bytes)")


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
