#!/usr/bin/env bash
# Builds the HUD's radar calibration from Valve's overview files, CS2's
# game/csgo/resource/overviews/<map>.txt. They frame the images the HUD draws
# on (s2r://panorama/images/overheadmaps/<map>_radar.psd), which no other
# calibration matches.
#
#   scripts/sync-radars.sh <overviews directory>    (or set CS2_OVERVIEWS)
#
# resolution is Valve's scale and offset is (-pos_x, 1024 * scale - pos_y), so a
# world point lands at ((x - pos_x) / scale, (pos_y - y) / scale) on the 1024
# square image. Every verticalsections entry but default becomes a level: CS2
# swaps to <map>_<level>_radar.psd while the player is below its AltitudeMax.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OVERVIEWS="${1:-${CS2_OVERVIEWS:-}}"

[ -d "$OVERVIEWS" ] || { echo "usage: $0 <CS2 resource/overviews directory> (or set CS2_OVERVIEWS)" >&2; exit 1; }

python3 -I - "$OVERVIEWS" "${ROOT}/shared/dotnet/FiveStack.Utilities/RadarMaps.cs" "${ROOT}/apps/utility-sw/hud/radars/metadata.json" <<'PY'
import json, os, re, sys

overviews, radar_maps, dst = sys.argv[1:4]

def parse(text):
    tokens = re.findall(r'"([^"]*)"|([{}])', re.sub(r'//[^\n]*', '', text))

    def block(i):
        out = {}
        while i < len(tokens) and tokens[i][1] != '}':
            key, (value, brace) = tokens[i][0].lower(), tokens[i + 1]
            if brace == '{':
                out[key], i = block(i + 2)
            else:
                out[key], i = value, i + 2
        return out, i + 1

    return block(0)[0]

def number(value):
    n = round(float(value), 6)
    return int(n) if n == int(n) else n

# The maps RadarMaps.All draws, so the file never calibrates one the HUD has no
# image rule for.
maps = re.findall(r'"([a-z0-9_]+)"', re.search(r'All = new\[\]\s*\{([^}]*)\}', open(radar_maps).read()).group(1))
out = {"_comment": "Built from CS2's resource/overviews/<map>.txt by scripts/sync-radars.sh: resolution = scale, offset = (-pos_x, 1024 * scale - pos_y), levels from verticalsections. Do not edit here."}

for name in maps:
    path = os.path.join(overviews, f"{name}.txt")
    if not os.path.isfile(path):
        sys.exit(f"no overview for {name} at {path}")

    overview = next(iter(parse(open(path).read()).values()))
    scale = float(overview["scale"])
    entry = {
        "resolution": number(scale),
        "offset": {"x": number(-float(overview["pos_x"])), "y": number(1024 * scale - float(overview["pos_y"]))},
    }
    levels = [
        {"name": level, "maxZ": number(section["altitudemax"])}
        for level, section in overview.get("verticalsections", {}).items()
        if level != "default"
    ]
    if levels:
        entry["levels"] = levels
    out[name] = entry

open(dst, "w").write(json.dumps(out, indent=2) + "\n")
print(f"calibration: {len(maps)} maps from {overviews}")
PY
