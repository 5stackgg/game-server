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

# CS2 resolves a workshop addon by its published file id, not by a friendly
# name: AddonsManager (and the engine) look for
# steamapps/workshop/content/730/<id>/<id>.vpk. A differently-named vpk
# downloads fine and then fails to mount, with the error pointing at a path that
# looks unrelated to the name you chose. WORKSHOP_ITEM_ID is unset only for the
# very first publish, before an id exists.
ADDON_NAME="${WORKSHOP_ITEM_ID:-5stack_utility_hud}"

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

# A running client keeps every layout and stylesheet it has loaded, by name,
# until it exits, so a dev build that reuses a name cannot be seen without a
# relaunch. With HUD_DEV_TAG set, a second copy of this build is compiled in
# which every file is renamed <stem>__<tag> and the references between them
# rewritten to match, so each one is new to the client.
# apps/utility-sw/scripts/dev.sh sets it; a publish never does.
#
# Beside the plain build, not instead of it: the plain names are what a plugin
# asks for until it is told a tagged build has reached the client, and they are
# the only ones packed.
TAGGED_SOURCE="${OUT_DIR}/tagged"
TAGGED_STAGE="${OUT_DIR}/stage-tagged"
rm -rf "${TAGGED_SOURCE}" "${TAGGED_STAGE}"

if [ -n "${HUD_DEV_TAG:-}" ]; then
	case "${HUD_DEV_TAG}" in
		*[!a-z0-9]*) die "HUD_DEV_TAG takes lowercase letters and digits only" ;;
	esac

	mkdir -p "${TAGGED_SOURCE}"
	cp -R "${HUD_DIR}/panorama/." "${TAGGED_SOURCE}/"

	renames="${OUT_DIR}/tagged.sed"
	: > "${renames}"

	find "${TAGGED_SOURCE}" -type f -path '*/custom_game/*' | while IFS= read -r file; do
		stem="$(basename "${file%.*}")"

		case "${stem}" in
			*[!A-Za-z0-9_]*) die "cannot tag ${file}: only letters, digits and _ in a name" ;;
		esac

		printf 's#custom_game/%s\\.#custom_game/%s__%s.#g\n' "${stem}" "${stem}" "${HUD_DEV_TAG}" >> "${renames}"
	done

	# References first, while every file still answers to its old name.
	find "${TAGGED_SOURCE}" -type f \( -name '*.xml' -o -name '*.css' \) | while IFS= read -r file; do
		sed -f "${renames}" "${file}" > "${file}.tagging"
		mv "${file}.tagging" "${file}"
	done

	find "${TAGGED_SOURCE}" -type f -path '*/custom_game/*' | while IFS= read -r file; do
		mv "${file}" "${file%.*}__${HUD_DEV_TAG}.${file##*.}"
	done

	log "compiling dev build ${HUD_DEV_TAG}"
	dotnet "${COMPILER_DLL}" compile-tree \
		--input-root "${TAGGED_SOURCE}" \
		--output-root "${TAGGED_STAGE}/panorama"
fi

# The loose copy scripts/hud-watch.sh mirrors into a local CS2 install: build/
# never crosses the codepier sync, and the machine with the game cannot unpack a
# vpk. Updated in place -- emptying the directory first would reach the watcher
# as every layout being deleted. The last dev build's tagged files go here, as
# this one's arrive.
mkdir -p "${DIST_DIR}/panorama"
cp -R "${STAGE_DIR}/panorama/." "${DIST_DIR}/panorama/"

if [ -d "${TAGGED_STAGE}/panorama" ]; then
	cp -R "${TAGGED_STAGE}/panorama/." "${DIST_DIR}/panorama/"
fi

find "${DIST_DIR}/panorama" -type f | while IFS= read -r loose; do
	relative="${loose#"${DIST_DIR}/panorama/"}"

	if [ ! -f "${STAGE_DIR}/panorama/${relative}" ] && [ ! -f "${TAGGED_STAGE}/panorama/${relative}" ]; then
		rm -f "${loose}"
	fi
done

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
	  scripts/hud-watch.sh still tests the loose copy in ${DIST_DIR}/panorama.
	EOF
fi
