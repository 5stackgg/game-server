#!/usr/bin/env bash
# Compiles the utility-practice Panorama sources into a mountable addon tree.
#
# PanoramaCompiler writes Source 2 containers directly, so this needs nothing
# but the .NET SDK -- no Windows, no CS2 install, no resourcecompiler.exe. That
# is the whole reason it can run in the same Linux image everything else builds
# in.
set -euo pipefail

HUD_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT_DIR="${HUD_DIR}/build"
STAGE_DIR="${OUT_DIR}/stage"
CACHE_DIR="${PANORAMA_COMPILER_DIR:-${HUD_DIR}/.compiler}"
TOOLS_DIR="${HUD_DIR}/.tools"
# Outside build/, which codepier does not sync: this is the copy that rides the
# mutagen session back to the local checkout.
DIST_DIR="${HUD_DIR}/dist"

# Pin the compiler: it writes a deliberately minimal resource profile and a
# change in what it emits is a change in what the client parses.
COMPILER_REPO="https://github.com/nicedayzhu/PanoramaCompiler.git"
COMPILER_REF="${PANORAMA_COMPILER_REF:-main}"

ADDON_NAME="5stack_utility_hud"

log() { printf '\033[36m==>\033[0m %s\n' "$1"; }
die() { printf '\033[31mx\033[0m %s\n' "$1" >&2; exit 1; }

VPKEDIT_VERSION="${VPKEDIT_VERSION:-v5.0.0.4}"

# The dev container and CI have no vpkeditcli, and the packing step is what turns
# compiled resources into something mountable. Fetched once into .tools rather
# than installed, so nothing is left on the machine that runs this.
ensure_vpkeditcli() {
	if command -v vpkeditcli >/dev/null 2>&1; then
		return 0
	fi

	if [ -x "${TOOLS_DIR}/vpkeditcli" ]; then
		PATH="${TOOLS_DIR}:${PATH}"
		return 0
	fi

	# Only Linux is auto-fetched: the macOS build ships as a .dmg that would have
	# to be mounted, which is not something a build script should do unasked.
	if [ "$(uname -s)" != "Linux" ]; then
		return 1
	fi

	log "fetching vpkeditcli ${VPKEDIT_VERSION}"
	mkdir -p "${TOOLS_DIR}"

	local url="https://github.com/craftablescience/VPKEdit/releases/download/${VPKEDIT_VERSION}/StrataSource-Linux-Binaries-gcc-Release.zip"

	if ! curl -fsSL "${url}" -o "${TOOLS_DIR}/tools.zip"; then
		log "could not download vpkeditcli"
		return 1
	fi

	unzip -o -q -j "${TOOLS_DIR}/tools.zip" '*vpkeditcli*' -d "${TOOLS_DIR}" || true
	rm -f "${TOOLS_DIR}/tools.zip"

	if [ ! -f "${TOOLS_DIR}/vpkeditcli" ]; then
		log "archive had no vpkeditcli"
		return 1
	fi

	chmod +x "${TOOLS_DIR}/vpkeditcli"
	PATH="${TOOLS_DIR}:${PATH}"
}

if [ ! -d "${CACHE_DIR}/.git" ]; then
	log "cloning PanoramaCompiler (${COMPILER_REF})"
	git clone --quiet "${COMPILER_REPO}" "${CACHE_DIR}"
fi

git -C "${CACHE_DIR}" fetch --quiet origin "${COMPILER_REF}"
git -C "${CACHE_DIR}" checkout --quiet FETCH_HEAD

COMPILER_DLL="${CACHE_DIR}/bin/Release/net10.0/PanoramaCompiler.dll"

if [ ! -f "${COMPILER_DLL}" ]; then
	log "building PanoramaCompiler"
	dotnet build "${CACHE_DIR}/PanoramaCompiler.csproj" -c Release --nologo -v quiet
fi

log "compiling panorama sources"
rm -rf "${STAGE_DIR}"
mkdir -p "${STAGE_DIR}"

# Compiled into stage/panorama/ and packed from stage/, so the vpk's internal
# tree keeps the panorama/ prefix. StrLayout resolves
# panorama/layout/custom_game/<name>.xml against the mounted search path root:
# pack from inside panorama/ instead and every lookup misses, with no error.
dotnet "${COMPILER_DLL}" compile-tree \
	--input-root "${HUD_DIR}/panorama" \
	--output-root "${STAGE_DIR}/panorama"

cp "${HUD_DIR}/addoninfo.txt" "${STAGE_DIR}/addoninfo.txt"

log "compiled:"
find "${STAGE_DIR}" -name '*_c' -o -name 'addoninfo.txt' | sed "s|${STAGE_DIR}/|  |"

# The client resolves panorama/layout/custom_game/nade_hud.vxml_c, so the tree
# above has to be packed at its root -- not nested under another directory.
if ensure_vpkeditcli; then
	# Packed into its own directory because that directory IS the workshop
	# content folder: anything else sitting beside the vpk gets uploaded too.
	log "packing ${ADDON_NAME}.vpk"
	rm -rf "${OUT_DIR}/upload"
	mkdir -p "${OUT_DIR}/upload"
	# --single-file keeps it to one .vpk instead of a _dir plus numbered chunks;
	# the whole addon is a few kilobytes of XML and CSS.
	vpkeditcli --single-file --no-progress \
		--output "${OUT_DIR}/upload/${ADDON_NAME}.vpk" "${STAGE_DIR}"

	# The prefix is the whole ballgame; a vpk without it mounts and resolves
	# nothing.
	# Captured, not piped: --file-tree writes to stderr, and under pipefail a
	# `| grep -q` SIGPIPEs the producer and fails the pipeline on a good vpk.
	tree="$(vpkeditcli --file-tree "${OUT_DIR}/upload/${ADDON_NAME}.vpk" 2>&1 || true)"

	case "${tree}" in
		*panorama*) ;;
		*) die "packed vpk is missing the panorama/ prefix" ;;
	esac
	mkdir -p "${DIST_DIR}"
	cp "${OUT_DIR}/upload/${ADDON_NAME}.vpk" "${DIST_DIR}/${ADDON_NAME}.vpk"

	log "content folder ready: ${OUT_DIR}/upload"
	log "synced copy:         ${DIST_DIR}/${ADDON_NAME}.vpk"
else
	cat <<-EOF

	  vpkeditcli unavailable, so the addon tree was built but not packed.
	  Install https://github.com/craftablescience/VPKEdit to produce the .vpk,
	  or point CS2 at ${STAGE_DIR} directly for a local override test.
	EOF
fi
