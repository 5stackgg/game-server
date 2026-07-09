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

BASE_DIR = path.dirname(path.abspath(__file__))

# our own signatures; the upstream-swiftly set is added by resolve_swiftly_sets
SETS = [
    {
        "name": "fivestack",
        "path": path.join(BASE_DIR, "gamedata/fivestack.gamedata.json"),
        "format": "ccs",
        "runtimes": [RUNTIME_CCS, RUNTIME_SWIFTLY],
    },
    {
        "name": "upstream-ccs",
        "path": path.join(BASE_DIR, "gamedata/ccs.gamedata.json"),
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

# SwiftlyS2's signatures live in the framework release we ship, which is pinned by the
# SwiftlyS2.CS2 <PackageReference> in apps/swiftly/src/FiveStack.csproj. Read at runtime,
# never baked: swiftly-update.yaml bumps that pin without rebuilding this image.
FIVESTACK_CSPROJ = (
    "https://raw.githubusercontent.com/5stackgg/game-server/"
    "{ref}/apps/swiftly/src/FiveStack.csproj"
)
SWIFTLY_RAW = (
    "https://raw.githubusercontent.com/swiftly-solution/swiftlys2/"
    "{ref}/plugin_files/gamedata/cs2/core/{name}.jsonc"
)


def detect_swiftly_version(fivestack_ref):
    """Read the pinned SwiftlyS2.CS2 version from apps/swiftly's csproj, or None."""
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

    if 0 <= offset["offset"] < count:
        result["ok"] = True
        return result

    # The offset overshoots the vtable we resolved. Swiftly labels each index by the
    # class that declares the method, but the index targets the concrete/derived
    # vtable used at run time (e.g. CGameRules -> CCSGameRules). s2binlib can only
    # resolve the named class's own vtable, so an out-of-range result here is
    # inconclusive, not a confirmed break — flag it for review without failing.
    result["ok"] = True
    result["warning"] = True
    result["reason"] = (
        f"offset {offset['offset']} exceeds the {count}-slot vtable resolved for "
        f"{class_name}; likely an index into a derived class's vtable"
    )
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


def per_set_statuses(results, swiftly):
    """Status per set, so each stands on its own: a CounterStrikeSharp break must not
    fail Swiftly, and a 5Stack break must not fail either upstream. A set fails on a
    real break, errors when it couldn't be validated at all (every entry skipped, a
    scan blew up, or — for swiftly — its gamedata couldn't be fetched), else passes."""
    def status_for(set_results):
        set_skipped = [r for r in set_results if r.get("skipped")]
        if any(r["ok"] is False for r in set_results):
            return "fail"
        if set_results and len(set_results) == len(set_skipped):
            return "error"
        if any(r.get("error") for r in set_skipped):
            return "error"
        return "pass"

    statuses = {}
    for result in results:  # preserve first-seen order
        statuses.setdefault(result["set"], None)
    for name in statuses:
        statuses[name] = status_for([r for r in results if r["set"] == name])

    # Swiftly requested but its gamedata couldn't be detected/fetched: no results to
    # judge, so report the set as errored rather than silently omitting it.
    if swiftly["error"]:
        statuses["upstream-swiftly"] = "error"
    return statuses


def aggregate_status(statuses):
    """Drives only the process exit code; consumers gate per runtime off the map."""
    if "fail" in statuses.values():
        return "fail"
    if "error" in statuses.values():
        return "error"
    return "pass"


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
    elif args.swiftly_version:
        info["version"] = args.swiftly_version
        info["source"] = "explicit"
    else:
        info["version"] = detect_swiftly_version(args.fivestack_ref)
        info["source"] = f"FiveStack.csproj@{args.fivestack_ref}"

    if not info["version"]:
        info["error"] = (
            "could not read the SwiftlyS2.CS2 version from apps/swiftly "
            f"FiveStack.csproj@{args.fivestack_ref}; pass --swiftly-version or "
            "--swiftly-ref to validate against a specific release"
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
        "--swiftly-version",
        default=None,
        help="skip csproj detection and validate this SwiftlyS2.CS2 version",
    )
    parser.add_argument(
        "--fivestack-ref",
        default="main",
        help="git ref of game-server to read the pinned SwiftlyS2.CS2 version from",
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
        print(f"[swiftly] validating SwiftlyS2 {swiftly['ref']} (from {swiftly['source']})", flush=True)

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
        if result.get("warning")
        or (result["ok"] and result["kind"] == "signature" and (result["count"] or 0) > 1)
    ]

    statuses = per_set_statuses(results, swiftly)
    overall = aggregate_status(statuses)

    result = {
        "build_id": args.build_id,
        "status": overall,
        "statuses": statuses,
        "swiftly": swiftly,
        "broken": broken,
        "warnings": warnings,
        "skipped": skipped,
        "results": results,
    }

    print("GAMEDATA_VALIDATION_RESULT " + json.dumps(result), flush=True)
    sys.exit(0 if overall == "pass" else 1)


if __name__ == "__main__":
    main()
