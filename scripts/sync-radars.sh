#!/usr/bin/env bash
# Re-mirrors the radar CALIBRATION from the web panel.
#
# Not the images. The HUD draws on CS2's own overheadmaps
# (s2r://panorama/images/overheadmaps/<map>_radar.psd) because a texture
# compiled by PanoramaCompiler's experimental image path never loaded in game.
# The web's radars are SimpleRadar, a drop-in replacement for Valve's, so the
# same calibration projects correctly onto either -- which is the only reason
# dropping the images cost nothing.
#
# The calibration itself still has to match the panel exactly: a lineup that
# lands on Window on the site and on Palace in game is worse than no map at all.
set -euo pipefail

WEB="${WEB_REPO:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../../web" && pwd)}"
HUD="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/apps/utility-sw/hud"
SRC="${WEB}/public/radars/metadata.json"

[ -f "$SRC" ] || { echo "no radar calibration at $SRC (set WEB_REPO)" >&2; exit 1; }

mkdir -p "${HUD}/radars"

python3 - "$SRC" "${HUD}/radars/metadata.json" <<'PY'
import json, sys
src, dst = sys.argv[1], sys.argv[2]
d = json.load(open(src)); d.pop("_comment", None)
out = {"_comment": "Mirrored from web/public/radars/metadata.json by scripts/sync-radars.sh. Do not edit here.", **d}
open(dst, "w").write(json.dumps(out, indent=2) + "\n")
print(f"calibration: {len(d)} maps")
PY

# The images are deliberately absent; a stale copy would be compiled into the
# addon and silently add ~2 MB of textures nothing references.
if [ -d "${HUD}/panorama/images" ]; then
	echo "warning: ${HUD}/panorama/images exists but nothing references it -- delete it" >&2
fi
