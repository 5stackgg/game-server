import argparse
import json
import sys
from os import path

import commentjson
import s2binlib

SETS = {
    "fivestack": "gamedata/fivestack.gamedata.json",
    "upstream-ccs": "gamedata/ccs.gamedata.json",
}


def load_entries(file_path):
    signatures = {}
    offsets = {}
    with open(file_path) as f:
        for name, entry in commentjson.load(f).items():
            if not isinstance(entry, dict):
                continue

            sig = entry.get("signatures")
            if sig:
                lib = sig.get("library") or sig.get("lib")
                if lib and sig.get("linux"):
                    signatures[name] = {"lib": lib, "linux": sig["linux"]}
                continue

            off = entry.get("offsets")
            lib = entry.get("library") or entry.get("lib")
            cls = entry.get("class")
            if off and lib and cls and off.get("linux") is not None:
                offsets[name] = {"lib": lib, "class": cls, "offset": off["linux"]}
    return signatures, offsets


def check_offset(set_name, name, off):
    result = {
        "set": set_name,
        "signature": name,
        "kind": "vtable",
        "class": off["class"],
        "offset": off["offset"],
        "count": None,
        "ok": False,
    }
    try:
        result["count"] = s2binlib.get_vfunc_count(off["lib"], off["class"])
    except Exception as error:
        result["error"] = str(error)
        return result
    result["ok"] = 0 <= off["offset"] < result["count"]
    return result


def main():
    parser = argparse.ArgumentParser(
        description="Validate CS2 gamedata signatures against a game install"
    )
    parser.add_argument("--game-path", default="/serverdata/serverfiles/game")
    parser.add_argument("--build-id", type=int, default=None)
    args = parser.parse_args()

    s2binlib.initialize(args.game_path, "csgo", "linux")

    results = []
    for set_name, file_path in SETS.items():
        if not path.exists(file_path):
            print(f"[skip] missing {file_path}", flush=True)
            continue
        signatures, offsets = load_entries(file_path)
        for name, sig in signatures.items():
            _, count = s2binlib.pattern_scan(sig["lib"], sig["linux"])
            results.append(
                {
                    "set": set_name,
                    "signature": name,
                    "kind": "signature",
                    "count": count,
                    "ok": count >= 1,
                }
            )
        for name, off in offsets.items():
            results.append(check_offset(set_name, name, off))

    broken = [r for r in results if not r["ok"]]
    warnings = [
        r
        for r in results
        if r["ok"] and r["kind"] == "signature" and (r["count"] or 0) > 1
    ]
    result = {
        "build_id": args.build_id,
        "status": "fail" if broken else "pass",
        "broken": broken,
        "warnings": warnings,
        "results": results,
    }

    print("GAMEDATA_VALIDATION_RESULT " + json.dumps(result), flush=True)
    sys.exit(1 if broken else 0)


if __name__ == "__main__":
    main()
