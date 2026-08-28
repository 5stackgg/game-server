#!/usr/bin/env bash
# Publishes the compiled HUD addon to the Steam Workshop through SteamCMD.
#
# UNVERIFIED FOR CS2. The normal route is the Windows-only Workshop Manager,
# which collects from a CS2 content tree and filters it through
# AddonConfig/VpkDirectories -- a whitelist that excludes panorama/layout and
# panorama/styles by default, so it happily publishes an empty item. This skips
# both by uploading the packed vpk directly. If app 730 rejects a SteamCMD
# upload, fall back to Workshop Manager and read the contents preview carefully.
set -euo pipefail

HUD_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT_DIR="${HUD_DIR}/build"
CONTENT_DIR="${OUT_DIR}/upload"
VDF="${OUT_DIR}/workshop.vdf"

# Public, so AddonsManager can fetch it by id over the anonymous workshop path.
# A private item is not downloadable by the server or by players.
VISIBILITY=0

# 0 creates a new item; set this to the id SteamCMD prints to update in place
# rather than publishing a second copy.
PUBLISHED_FILE_ID="${WORKSHOP_ITEM_ID:-0}"

STEAM_USER="${STEAM_USER:-}"
PREVIEW="${HUD_DIR}/preview.jpg"
[ -f "${PREVIEW}" ] || PREVIEW="${HUD_DIR}/preview.png"

log() { printf '\033[36m==>\033[0m %s\n' "$1"; }
die() { printf '\033[31mx\033[0m %s\n' "$1" >&2; exit 1; }

[ -n "${STEAM_USER}" ] || die "set STEAM_USER to the Steam account that owns the item"
command -v steamcmd >/dev/null 2>&1 || die "steamcmd not on PATH"

if [ ! -d "${CONTENT_DIR}" ]; then
	log "no content folder, building"
	"${HUD_DIR}/build.sh"
fi

[ -d "${CONTENT_DIR}" ] || die "build.sh produced no ${CONTENT_DIR} (is vpkeditcli installed?)"

# An item with no vpk publishes fine and does nothing, which is the failure this
# whole file exists to avoid.
find "${CONTENT_DIR}" -name '*.vpk' | grep -q . || die "no .vpk in ${CONTENT_DIR}"

[ -f "${PREVIEW}" ] || die "workshop items need a preview image at ${HUD_DIR}/preview.{jpg,png}"
[ "$(wc -c < "${PREVIEW}")" -le 1000000 ] || die "preview exceeds Steam's 1 MB limit: ${PREVIEW}"

cat > "${VDF}" <<EOF
"workshopitem"
{
	"appid"           "730"
	"publishedfileid" "${PUBLISHED_FILE_ID}"
	"contentfolder"   "${CONTENT_DIR}"
	"previewfile"     "${PREVIEW}"
	"visibility"      "${VISIBILITY}"
	"title"           "5Stack Utility HUD"
	"description"     "On-screen grenade lineup guidance for 5stack practice servers. Panorama resources for the utility-practice plugin; no effect without it."
	"changenote"      "$(git -C "${HUD_DIR}" rev-parse --short HEAD 2>/dev/null || echo local)"
}
EOF

log "publishing from ${CONTENT_DIR}"
cat "${VDF}"

steamcmd +login "${STEAM_USER}" +workshop_build_item "${VDF}" +quit

cat <<-EOF

	If SteamCMD printed a PublishedFileID, record it:

	  WORKSHOP_ITEM_ID=<id> ./publish.sh    # updates in place next time

	Then point the server at it via AddonsManager's config, and confirm the item
	is Public on its workshop page.
EOF
