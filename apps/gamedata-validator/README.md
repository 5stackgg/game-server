## 5Stack Gamedata Validator

5Stack is a platform for organizing and managing competitive CS2 matches and tournaments.

Please visit [5Stack](https://docs.5stack.gg) for more documentation.

## What it validates

Scans a CS2 install's Linux binaries and checks that every byte-pattern signature still
resolves, and that every vtable offset is still in bounds. It covers both game server
runtimes, since 5Stack runs Swiftly and CounterStrikeSharp side by side:

| Set | Runtime | Source |
| --- | --- | --- |
| `fivestack` | both | `gamedata/fivestack.gamedata.json` (vendored from `game-server/`) |
| `upstream-ccs` | CounterStrikeSharp | fetched at image build from CounterStrikeSharp |
| `upstream-swiftly` | Swiftly | fetched at run time for the version pinned in `swiftly-game-server` |

Entries in `fivestack.gamedata.json` may carry a `runtimes` key naming the runtimes that
actually use them — the Swiftly port hooks `ConnectClient` but not the two vtable offsets.
Everything else defaults to its set's runtime.

`--runtime swiftlys2` or `--runtime counterstrikesharp` narrows a run to one runtime.

### SwiftlyS2 version matching

The `upstream-swiftly` gamedata is not pinned at image build. When the `swiftlys2` runtime
is in scope, the validator reads the `SwiftlyS2.CS2` `<PackageReference>` version from
`swiftly-game-server`'s `src/FiveStack.csproj` on GitHub — the release we actually ship —
and fetches that version's gamedata from SwiftlyS2 at run time, so it tracks the source of
truth rather than a ref that could drift. This needs network access during the run.

`--fivestack-ref <ref>` reads the csproj from a different branch/tag (default `main`);
`--swiftly-ref <tag>` skips csproj detection and validates a specific SwiftlyS2 release. If
the version can't be read, the run reports `error`. The version, resolved git ref, and
source are reported under `swiftly` in the result JSON.

Results print as a single `GAMEDATA_VALIDATION_RESULT {json}` line. Each set is judged on
its own under `statuses` (`{"fivestack": "pass", "upstream-ccs": "fail", "upstream-swiftly":
"pass"}`) so a break in one runtime never taints another — a CounterStrikeSharp break won't
fail Swiftly, and a 5Stack break won't fail either upstream. A set is `fail` on a real
break, `error` when nothing in it could be validated, else `pass`. The top-level `status` is
the aggregate (`fail` > `error` > `pass`) and drives only the process exit code (nonzero
unless every set passed); consumers gate per runtime off `statuses`.

Entries that can't be checked — a member offset rather than a vtable index, or a class with
no resolvable vtable — land in `skipped` and never fail a set. A vtable offset that overshoots
its resolved (base-class) vtable is reported as a `warning`, not a break: the index targets
the concrete/derived vtable used at run time, which s2binlib can't resolve from the base.

## Credits

- Adapted from [swiftly-solution/gamedata-validator](https://github.com/swiftly-solution/gamedata-validator).
- Pattern scanning via [swiftly-solution/s2binlib](https://github.com/swiftly-solution/s2binlib) (`lib/s2binlib.so`).
- Validates [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp) and [SwiftlyS2](https://github.com/swiftly-solution/swiftlys2) gamedata.
