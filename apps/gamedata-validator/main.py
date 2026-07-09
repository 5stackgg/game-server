import argparse
import json
import re
import sys
import tempfile
import urllib.request
from os import path

import json5
import s2binlib

RUNTIME_CCS = "counterstrikesharp"
RUNTIME_SWIFTLY = "swiftlys2"

# Sets that don't depend on the running SwiftlyS2 version. The upstream-swiftly
# set is added at runtime (see resolve_swiftly_sets): its gamedata is fetched for
# the version swiftly-game-server pins, not a version pinned at image build.
SETS = [
    {
        "name": "fivestack",
        "path": "gamedata/fivestack.gamedata.json",
        "format": "ccs",
        "runtimes": [RUNTIME_CCS, RUNTIME_SWIFTLY],
    },
    {
        "name": "upstream-ccs",
        "path": "gamedata/ccs.gamedata.json",
        "format": "ccs",
        "runtimes": [RUNTIME_CCS],
    },
]

# name -> loader format, for the three core SwiftlyS2 gamedata files.
SWIFTLY_GAMEDATA = [
    ("signatures", "swiftly-signatures"),
    ("offsets", "swiftly-offsets"),
    ("patches", "swiftly-patches"),
]

# The SwiftlyS2 version we ship is pinned by the SwiftlyS2.CS2 <PackageReference> in
# swiftly-game-server's FiveStack.csproj — the authoritative "what we run", independent
# of any particular game install. Read it straight from the repo so the validator
# tracks the source of truth rather than a copy that can drift.
FIVESTACK_CSPROJ = (
    "https://raw.githubusercontent.com/5stackgg/swiftly-game-server/"
    "{ref}/src/FiveStack.csproj"
)
SWIFTLY_RAW = (
    "https://raw.githubusercontent.com/swiftly-solution/swiftlys2/"
    "{ref}/plugin_files/gamedata/cs2/core/{name}.jsonc"
)


def detect_swiftly_version(fivestack_ref):
    """Read the pinned SwiftlyS2.CS2 version from swiftly-game-server's csproj, or None."""
    try:
        with urllib.request.urlopen(FIVESTACK_CSPROJ.format(ref=fivestack_ref), timeout=20) as resp:
            text = resp.read().decode("utf-8", "replace")
    except Exception:
        return None
    # one PackageReference per line; attribute order isn't guaranteed, so match the
    # element carrying Include="SwiftlyS2.CS2" and pull its Version.
    for line in text.splitlines():
        if 'Include="SwiftlyS2.CS2"' in line:
            match = re.search(r'Version="([^"]+)"', line)
            if match:
                return match.group(1)
    return None


def swiftly_refs(version):
    """Git refs to try for a detected version, tag form ('v...') first."""
    refs = []
    for ref in ((version if version.startswith("v") else "v" + version), version):
        if ref not in refs:
            refs.append(ref)
    return refs


def fetch_swiftly_gamedata(version, dest_dir):
    """Download the version's signatures/offsets/patches. Returns the ref used, or None."""
    for ref in swiftly_refs(version):
        try:
            for name, _format in SWIFTLY_GAMEDATA:
                urllib.request.urlretrieve(
                    SWIFTLY_RAW.format(ref=ref, name=name),
                    path.join(dest_dir, f"swiftly.{name}.jsonc"),
                )
            return ref
        except Exception:
            continue
    return None

# Swiftly's offsets never name the binary their class lives in, so the vtable has
# to be found by probing each module until one resolves.
LIB_CANDIDATES = (
    "server",
    "engine2",
    "tier0",
    "networksystem",
    "schemasystem",
    "soundsystem",
    "materialsystem2",
    "client",
)

_vfunc_counts = {}


def load_raw(file_path):
    # json5, not commentjson: Swiftly's offsets.jsonc mixes block comments with
    # trailing commas, which commentjson's grammar rejects outright.
    with open(file_path) as file:
        return json5.load(file)


def load_ccs(raw):
    signatures = []
    offsets = []
    for name, entry in raw.items():
        if not isinstance(entry, dict):
            continue

        runtimes = entry.get("runtimes")

        signature = entry.get("signatures")
        if isinstance(signature, dict):
            lib = signature.get("library") or signature.get("lib")
            pattern = signature.get("linux")
            if lib and isinstance(pattern, str):
                signatures.append(
                    {
                        "name": name,
                        "lib": lib,
                        "pattern": pattern,
                        "runtimes": runtimes,
                    }
                )
            continue

        offset = entry.get("offsets")
        if isinstance(offset, dict) and isinstance(offset.get("linux"), int):
            offsets.append(
                {
                    "name": name,
                    "lib": entry.get("library") or entry.get("lib"),
                    "class": entry.get("class"),
                    "offset": offset["linux"],
                    "runtimes": runtimes,
                }
            )

    return {"signatures": signatures, "offsets": offsets, "patches": []}


def load_swiftly_signatures(raw):
    signatures = []
    for name, entry in raw.items():
        if not isinstance(entry, dict):
            continue
        lib = entry.get("lib") or entry.get("library")
        pattern = entry.get("linux")
        if lib and isinstance(pattern, str):
            signatures.append({"name": name, "lib": lib, "pattern": pattern})
    return {"signatures": signatures, "offsets": [], "patches": []}


def load_swiftly_offsets(raw):
    offsets = []
    for name, entry in raw.items():
        if not isinstance(entry, dict):
            continue
        if isinstance(entry.get("linux"), int):
            offsets.append(
                {
                    "name": name,
                    "lib": entry.get("lib") or entry.get("library"),
                    "class": None,
                    "offset": entry["linux"],
                }
            )
    return {"signatures": [], "offsets": offsets, "patches": []}


def load_swiftly_patches(raw):
    patches = []
    for name, entry in raw.items():
        if isinstance(entry, dict) and entry.get("signature"):
            patches.append({"name": name, "signature": entry["signature"]})
    return {"signatures": [], "offsets": [], "patches": patches}


LOADERS = {
    "ccs": load_ccs,
    "swiftly-signatures": load_swiftly_signatures,
    "swiftly-offsets": load_swiftly_offsets,
    "swiftly-patches": load_swiftly_patches,
}


def vfunc_count(lib, class_name):
    key = (lib, class_name)
    if key not in _vfunc_counts:
        try:
            _vfunc_counts[key] = s2binlib.get_vfunc_count(lib, class_name)
        except Exception:
            _vfunc_counts[key] = None
    return _vfunc_counts[key]


def resolve_vtable(class_name, lib=None):
    candidates = [lib] if lib else []
    candidates += [candidate for candidate in LIB_CANDIDATES if candidate != lib]
    for candidate in candidates:
        count = vfunc_count(candidate, class_name)
        if count:
            return candidate, count
    return None, None


def offset_class(offset):
    """The class to bound-check against, or None when the offset isn't a vtable index.

    Two shapes get rejected. `Class::m_Member` is a byte offset into the object
    (Swiftly stores these alongside vtable indexes, and they run into the
    thousands), and an underscore name like `CTakeDamageInfo_HitGroup` is just as
    often a member offset too. Bound-checking either against a vfunc count reports
    a break that isn't there.
    """
    if offset["class"]:
        return offset["class"]
    if "::" not in offset["name"]:
        return None
    class_name, member = offset["name"].split("::", 1)
    if member.startswith("m_"):
        return None
    return class_name


def result_for(set_name, runtimes, name, kind):
    return {
        "set": set_name,
        "runtimes": runtimes,
        "signature": name,
        "kind": kind,
        "count": None,
        "ok": None,
    }


def skip(result, reason, error=None):
    result["skipped"] = True
    result["reason"] = reason
    if error:
        result["error"] = error
    return result


def check_signature(set_name, runtimes, signature):
    result = result_for(set_name, runtimes, signature["name"], "signature")
    result["lib"] = signature["lib"]

    try:
        _, count = s2binlib.pattern_scan(signature["lib"], signature["pattern"])
    except Exception as error:
        return skip(result, f"could not scan {signature['lib']}", str(error))

    result["count"] = count
    result["ok"] = count >= 1
    return result


def check_offset(set_name, runtimes, offset):
    result = result_for(set_name, runtimes, offset["name"], "vtable")
    result["offset"] = offset["offset"]

    class_name = offset_class(offset)
    if not class_name:
        return skip(result, "not a vtable index")

    result["class"] = class_name

    lib, count = resolve_vtable(class_name, offset["lib"])
    if not count:
        return skip(result, f"no vtable found for {class_name}")

    result["lib"] = lib
    result["count"] = count
    result["ok"] = 0 <= offset["offset"] < count
    return result


def check_patch(set_name, runtimes, patch, signature_names):
    result = result_for(set_name, runtimes, patch["name"], "patch")
    result["signature_ref"] = patch["signature"]
    result["ok"] = patch["signature"] in signature_names
    return result


def entry_runtimes(entry, spec):
    runtimes = entry.get("runtimes")
    if isinstance(runtimes, list) and runtimes:
        return runtimes
    return spec["runtimes"]


def resolve_swiftly_sets(args):
    """Detect the running SwiftlyS2 version and fetch its gamedata, so the
    upstream-swiftly checks run against the version actually installed rather
    than one pinned at image build. Returns (sets, info)."""
    info = {"version": None, "ref": None, "source": None, "error": None}
    if args.runtime not in ("all", RUNTIME_SWIFTLY):
        return [], info  # swiftly out of scope; don't touch the network

    if args.swiftly_ref:
        info["version"] = args.swiftly_ref
        info["source"] = "override"
    else:
        info["version"] = detect_swiftly_version(args.fivestack_ref)
        info["source"] = f"FiveStack.csproj@{args.fivestack_ref}"

    if not info["version"]:
        info["error"] = (
            "could not read the SwiftlyS2.CS2 version from swiftly-game-server "
            f"FiveStack.csproj@{args.fivestack_ref}; pass --swiftly-ref to validate "
            "against a specific release"
        )
        return [], info

    dest = tempfile.mkdtemp(prefix="swiftly-gd-")
    info["ref"] = fetch_swiftly_gamedata(info["version"], dest)
    if not info["ref"]:
        info["error"] = f"could not fetch SwiftlyS2 gamedata for {info['version']}"
        return [], info

    sets = [
        {
            "name": "upstream-swiftly",
            "path": path.join(dest, f"swiftly.{name}.jsonc"),
            "format": fmt,
            "runtimes": [RUNTIME_SWIFTLY],
        }
        for name, fmt in SWIFTLY_GAMEDATA
    ]
    return sets, info


def main():
    parser = argparse.ArgumentParser(
        description="Validate CS2 gamedata signatures against a game install"
    )
    parser.add_argument("--game-path", default="/serverdata/serverfiles/game")
    parser.add_argument("--build-id", type=int, default=None)
    parser.add_argument(
        "--runtime",
        choices=["all", RUNTIME_CCS, RUNTIME_SWIFTLY],
        default="all",
        help="only validate gamedata used by this game server runtime",
    )
    parser.add_argument(
        "--fivestack-ref",
        default="main",
        help="git ref of swiftly-game-server to read the pinned SwiftlyS2.CS2 version from",
    )
    parser.add_argument(
        "--swiftly-ref",
        default=None,
        help="override version detection and fetch SwiftlyS2 gamedata at this git ref instead",
    )
    args = parser.parse_args()

    s2binlib.initialize(args.game_path, "csgo", "linux")

    swiftly_sets, swiftly = resolve_swiftly_sets(args)
    if swiftly["error"]:
        print(f"[swiftly] {swiftly['error']}", flush=True)
    elif swiftly["ref"]:
        print(f"[swiftly] validating {swiftly['version']} @ {swiftly['ref']}", flush=True)

    loaded = []
    signature_names = {}
    for spec in SETS + swiftly_sets:
        if not path.exists(spec["path"]):
            print(f"[skip] missing {spec['path']}", flush=True)
            continue
        entries = LOADERS[spec["format"]](load_raw(spec["path"]))
        loaded.append((spec, entries))
        signature_names.setdefault(spec["name"], set()).update(
            signature["name"] for signature in entries["signatures"]
        )

    def selected(entry, spec):
        runtimes = entry_runtimes(entry, spec)
        if args.runtime != "all" and args.runtime not in runtimes:
            return None
        return runtimes

    results = []
    for spec, entries in loaded:
        for signature in entries["signatures"]:
            runtimes = selected(signature, spec)
            if runtimes:
                results.append(check_signature(spec["name"], runtimes, signature))

        for offset in entries["offsets"]:
            runtimes = selected(offset, spec)
            if runtimes:
                results.append(check_offset(spec["name"], runtimes, offset))

        for patch in entries["patches"]:
            runtimes = selected(patch, spec)
            if runtimes:
                results.append(
                    check_patch(
                        spec["name"],
                        runtimes,
                        patch,
                        signature_names.get(spec["name"], set()),
                    )
                )

    broken = [result for result in results if result["ok"] is False]
    skipped = [result for result in results if result.get("skipped")]
    warnings = [
        result
        for result in results
        if result["ok"] and result["kind"] == "signature" and (result["count"] or 0) > 1
    ]

    result = {
        "build_id": args.build_id,
        "status": "fail" if broken else "pass",
        "swiftly": swiftly,
        "broken": broken,
        "warnings": warnings,
        "skipped": skipped,
        "results": results,
    }

    # A scan that blew up says nothing about the signature, so it can't be a
    # break — but neither is it a pass, and reporting green would hide it.
    # Skips with no error (a member offset, an unresolvable class) are expected.
    unscannable = [entry for entry in skipped if entry.get("error")]

    if not broken:
        # SwiftlyS2 was in scope but we couldn't determine or fetch its gamedata:
        # validating nothing about it and reporting green would hide exactly the
        # drift this check exists to catch.
        if swiftly["error"]:
            result["status"] = "error"
            result["error"] = swiftly["error"]
        elif len(results) == len(skipped):
            result["status"] = "error"
            result["error"] = "no gamedata entries could be validated"
        elif unscannable:
            result["status"] = "error"
            result["error"] = (
                f"{len(unscannable)} entries could not be scanned: "
                f"{unscannable[0]['error']}"
            )

    print("GAMEDATA_VALIDATION_RESULT " + json.dumps(result), flush=True)
    sys.exit(1 if result["status"] != "pass" else 0)


if __name__ == "__main__":
    main()
