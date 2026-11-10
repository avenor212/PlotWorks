#!/usr/bin/env python3
"""Turn a PolyCut G-code file back into a .polycut project.

The test plates were only ever kept as G-code, so there was no project to open and
therefore no way to see on the bed where the paper had to go. Rebuilding the geometry
from the toolpath is exact by construction: what the app shows is literally the path the
pen will follow.

    python tools/gcode-to-project.py smoke-test.gcode smoke-test.polycut
    python tools/gcode-to-project.py pen-test-plate.gcode pen-test-plate.polycut \
        --template smoke-test.polycut --name "笔写测试板"

The .polycut container is GZip-compressed JSON (see ProjectSerializationService).
"""

import argparse
import datetime as dt
import gzip
import json
import re
import uuid

# A1 bed. Canvas coordinates run the other way in Y: canvas_y = bed_height - gcode_y.
DEFAULT_BED_HEIGHT = 256.0

GROUP_ID = uuid.uuid4()

CONFIG_TEMPLATE = """{
  "Area": "\\u00220,0,256,256\\u0022",
  "Version": 0.2,
  "SoftwareVersion": "1.0.0",
  "Name": "Configuration",
  "SelectedToolMode": 1,
  "Tolerance": 0.01,
  "AutoUnionText": true,
  "WorkSpeed": 40,
  "TravelSpeed": 60,
  "ZSpeed": 10,
  "WorkZ": %(work_z)s,
  "TravelZ": %(travel_z)s,
  "SafeZ": 10,
  "Passes": 1,
  "PassHeightDelta": 0,
  "WorkAreaWidth": 256,
  "WorkAreaHeight": 256,
  "ToolOffsetX": 0,
  "ToolOffsetY": 0,
  "CutOffsetX": 0,
  "CutOffsetY": 0,
  "DrawOffsetX": 0,
  "DrawOffsetY": 0,
  "ToolDownGCode": "",
  "ToolUpGCode": "",
  "CuttingConfig": {"ToolRadius": 0.45, "ToolDiameter": 0.9, "Overcut": 0, "BladePathNoiseTolerance": 0.05, "CutWidthCompensation": 0},
  "DrawingConfig": {"MinStrokeWidth": 1, "ShadingAngle": 45, "KeepOutlines": true, "FillType": 0, "AllowDrawingOverOutlines": true, "MaxStrokeWidth": 1, "ShadingThreshold": 1, "OutlinesBeforeFill": false, "PenWidth": 0.4, "PenWidthCompensation": false, "FillOutlineOverlap": 100},
  "ExportConfig": {"DestinationIP": "http://klipper.local/", "Type": 0, "FileDestination": null, "DestinationPort": 7125, "AutoPrint": false},
  "OptimisedToolPath": true,
  "ExtractOneColour": false,
  "ExtractionColor": "",
  "SkipBoundsCheck": false,
  "ClipToBounds": false,
  "SimplifyToolPaths": true,
  "SimplifyMergeAngle": 0.1,
  "SmallFeatureMergeThreshold": 0.05
}"""


def parse_gcode(path):
    """Return a list of polylines in G-code space (millimetres, machine Y)."""
    with open(path, "r", encoding="utf-8-sig") as handle:
        lines = handle.readlines()

    polylines = []
    current = []
    # Position the head is at, carried across moves (G-code is modal).
    x = y = 0.0
    pen_down = False

    def flush():
        if len(current) >= 2:
            polylines.append(list(current))
        current.clear()

    for raw in lines:
        line = raw.strip()
        if not line or line.startswith(";"):
            # End of the drawing section; everything after is the end sequence.
            if "Custom End GCode" in line:
                break
            continue
        if not line.startswith(("G0", "G1")):
            continue

        words = dict(re.findall(r"([XYZF])\s*(-?\d+(?:\.\d+)?)", line))
        if not words:
            continue

        nx = float(words["X"]) if "X" in words else None
        ny = float(words["Y"]) if "Y" in words else None
        nz = float(words["Z"]) if "Z" in words else None

        is_move = line.startswith("G0")

        if is_move:
            # Any travel ends the stroke in progress.
            flush()
            pen_down = False
            if nx is not None:
                x = nx
            if ny is not None:
                y = ny
            continue

        # G1: plotting move.
        if not pen_down:
            pen_down = True
            if not current:
                current.append((x, y))
        if nx is not None:
            x = nx
        if ny is not None:
            y = ny
        current.append((x, y))

        if nz is not None and nz > 0:
            # A Z change inside a G1 (rare) also ends the stroke.
            flush()
            pen_down = False

    flush()
    return polylines


def to_canvas(polylines, bed_height):
    """G-code Y grows away from the operator; canvas Y grows towards the bottom."""
    return [[(px, bed_height - py) for (px, py) in line] for line in polylines]


def build_path_data(polylines):
    parts = []
    for line in polylines:
        first = line[0]
        parts.append(f"M {first[0]:.4f},{first[1]:.4f}")
        for px, py in line[1:]:
            parts.append(f"L {px:.4f},{py:.4f}")
    return " ".join(parts)


def load_config_text(template_path, work_z, travel_z):
    """Reuse the machine's real configuration from an existing project.

    Inside the project file `Configuration` is itself a JSON string, so it is carried
    over verbatim rather than re-serialised - only the two Z heights are forced to the
    values the G-code actually used, otherwise reopening the project would silently
    switch the pen back to a different depth.
    """
    if not template_path:
        return CONFIG_TEMPLATE % {"work_z": work_z, "travel_z": travel_z}

    with gzip.open(template_path, "rt", encoding="utf-8-sig") as handle:
        template = json.load(handle)
    config_text = template.get("Configuration")
    if not config_text:
        return CONFIG_TEMPLATE % {"work_z": work_z, "travel_z": travel_z}

    cfg = json.loads(config_text)
    cfg["WorkZ"] = work_z
    cfg["TravelZ"] = travel_z
    return json.dumps(cfg, ensure_ascii=False, indent=2)


def main():
    parser = argparse.ArgumentParser(description="Rebuild a .polycut project from G-code")
    parser.add_argument("gcode")
    parser.add_argument("out")
    parser.add_argument("--name", default=None, help="drawable name shown in the element list")
    parser.add_argument("--bed-height", type=float, default=DEFAULT_BED_HEIGHT)
    parser.add_argument("--work-z", type=float, default=5.0, help="pen-down Z used in the file")
    parser.add_argument("--travel-z", type=float, default=10.0, help="pen-up Z used in the file")
    parser.add_argument("--template", default=None, help="existing .polycut to copy machine settings from")
    args = parser.parse_args()

    polylines = parse_gcode(args.gcode)
    if not polylines:
        raise SystemExit("no plotting moves found - is this a PolyCut G-code file?")

    canvas = to_canvas(polylines, args.bed_height)

    xs = [p[0] for line in canvas for p in line]
    ys = [p[1] for line in canvas for p in line]
    left, right = min(xs), max(xs)
    top, bottom = min(ys), max(ys)

    name = args.name or "刀路（由 G-code 还原）"
    drawable_id = uuid.uuid4()

    project = {
        "Version": "1.0",
        "CreatedDate": dt.datetime.now().astimezone().isoformat(),
        "ModifiedDate": dt.datetime.now().astimezone().isoformat(),
        "Drawables": [{
            "GeometricBounds": True,
            "Id": str(drawable_id),
            "Name": name,
            "Type": "Path",
            "Left": left,
            "Top": top,
            "Width": max(right - left, 0.0001),
            "Height": max(bottom - top, 0.0001),
            "RotationAngle": 0,
            "AffineTransform": "Identity",
            "TransformOriginX": 0.5,
            "TransformOriginY": 0.5,
            "ScaleX": 1,
            "ScaleY": 1,
            "IsHidden": False,
            "IsRegistrationMark": False,
            "StrokeColor": "#FF000000",
            "StrokeThickness": 0.5,
            "FillColor": "#00FFFFFF",
            "PathData": build_path_data(canvas),
            "PathTransform": None,
            "TextContent": None,
            "FontFamily": None,
            "FontSize": 0,
            "LineX1": 0,
            "LineY1": 0,
            "LineX2": 0,
            "LineY2": 0,
            "ParentGroupId": str(GROUP_ID),
        }],
        "Groups": [{
            "Id": str(GROUP_ID),
            "Name": "Drawing Group",
            "GroupType": "DrawableGroup",
            "ChildIds": [str(drawable_id)],
            "ParentGroupId": None,
            "Left": 0,
            "Top": 0,
            "Width": 0,
            "Height": 0,
            "RotationAngle": 0,
            "AffineTransform": "Identity",
            "TransformOriginX": 0.5,
            "TransformOriginY": 0.5,
            "ScaleX": 1,
            "ScaleY": 1,
            "IsHidden": True,
            "NativeWidth": 0,
            "NativeHeight": 0,
        }],
        "ReferencePoints": [],
        "Configuration": load_config_text(args.template, args.work_z, args.travel_z),
        "FontSizeUnit": "mm",
    }

    with gzip.open(args.out, "wt", encoding="utf-8") as handle:
        json.dump(project, handle, ensure_ascii=False, indent=2)

    print(f"wrote {args.out}")
    print(f"  strokes : {len(polylines)}")
    print(f"  points  : {sum(len(line) for line in polylines)}")
    print(f"  canvas  : X {left:.2f}..{right:.2f}  Y {top:.2f}..{bottom:.2f}")
    print(f"  gcode Y : {args.bed_height - bottom:.2f}..{args.bed_height - top:.2f}")


if __name__ == "__main__":
    main()
