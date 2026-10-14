# utility-practice HUD

The Panorama half of the practice plugin. `panorama/` is authored here and
compiled into a workshop addon the client mounts; the C# in `../src/Hud/` drives
it through `CCSCustomHudLayout`.

| Layout | Captures input | What it is |
| --- | --- | --- |
| `nade3_bar` | no | Everything on screen while you play: a compass with a grenade icon per spot, coloured by type, what the keys do right now, and what the last key did. On the right, what utility is out and for how long, and the plan or stack being worked through. In the middle, for a few seconds, what a flash did. |
| `nade2_hud` | no | The throw card, bottom right: where it lands, a drawn mouse and keycaps for how to throw it, the run-up, live aim error, and how far along the run of landed throws that counts as mastered. |
| `nade2_wheel` | yes | TAB. Eight wedges around a hub, an outer ring of two more over each, and wedges that open a wheel of their own. Beside it, what the keys do. Generated with `nadewheel2.css` by `wheel.py`: edit that and run it, never the two files. |
| `nade2_pick` | yes | Choosing a lineup: the map with a marker per landing spot beside the list, under one set of filter chips. |
| `nade2_run` | no | The execute behind `.playbook`. Who throws what and when, yours in colour and everyone else's greyed. |
| `nade3_edit` | yes | Rename, describe, change visibility. Fields ask for their value in chat. |

All six include `nadehud2.css`, and the wheel `nadewheel2.css` as well: it
uses things no other layout does, and a stylesheet the game rejects takes every
layout that includes it down with it. The bar adds `nadebar3.css` after it, for
what changed on the compass once `nadehud2.css` was published. `nade_hud`, `nade_list`, `nade_map`, `nade_run`,
`nade_edit` and `nadehud.css` are the first published set, kept untouched for
servers still on a plugin that asks for them; nothing in this repo drives them
any more. Neither does it drive `nade2_edit`, superseded by `nade3_edit` because its
buttons used classes `nadehud2.css` never styled, nor `nade2_bar`, superseded by
`nade3_bar` because every compass pip was the same grey. `HudSlots.cs` has the rule for when a name has to change.

Passive layouts must never capture input — a panel that takes the cursor while
somebody is aiming takes the shot with it.

## The slot contract

`custom_hud_layout` fails silently in both directions. A dialog variable the
layout does not declare renders nothing; a class nothing styles changes nothing.
Neither logs.

So every name the server is allowed to write lives in
`shared/dotnet/FiveStack.Utilities/HudSlots.cs`, and `HudLayoutContractTests`
asserts against the files in this directory that:

- the `{s:…}` variables and `HudSlots` name the same set, in both directions;
- every `Button id` the layout declares is one the code expects, and vice versa;
- every element the server toggles a class on exists;
- every class the server toggles is actually selected by a rule in the
  stylesheet;
- the one-of-N class groups (`x0…x63`, `y0…y63`, `p0…p10`) are declared for
  exactly the counts `HudSlots` will pick between;
- every map with a `#radar.<map>` rule also has calibration in `radars/`;
- every `PracticeStepColors` name has a chip and a swatch rule, and none of them
  is ever green or red — those two belong to the aim ramp;
- `nade3_edit` contains no geometry field, so editing where a smoke lands can
  never arrive by accident on the back of a rename.

Rename a slot in one place and `dotnet test apps/utility-sw/test` fails. That is
the point.

## Positions Panorama will not take

A dialog variable carries text, not geometry, so a map marker and the drill meter
cannot be positioned by one. Both are one-of-N class groups: the stylesheet
declares every cell, the server picks one, `HudSurface.Pick` swaps between them.

`margin-left` and `margin-top` are different properties, so a marker carries both
grids at once and needs no wrapper. An earlier version hung each marker off
nested zero-size anchor panels — **Panorama lays out nothing inside a parent with
no size**, so the markers existed, were positioned correctly, and never drew.
`overflow: noclip` does not help; there is no layout to un-clip. Do not
reintroduce a zero-size parent.

## Saying it, not just showing it

Every element states its own meaning. The aim reticle was once a tolerance box
with a moving dot: geometrically perfect, and nobody could tell what it was for.
It is now `LOOK RIGHT AND DOWN` in words, with the error in degrees, keyed to the
lineup's own `aim_tolerance` so colour, text and the audio cue can never
disagree. A display that can only be read by someone who knows how it was built
will generate bug reports about something innocent.

## Building

In the `dev-utility-swiftly-game-server` pod this runs on its own:
`apps/utility-sw/scripts/dev.sh` builds the addon at startup and rebuilds it
whenever anything under `panorama/` changes, alongside the plugin's own
hot-reload. A broken layout logs to `/tmp/hud-build.log` and never stops the C#
from reloading.

The addon half is **not** hot-reloadable, though — the layouts have to reach a
game *client*, which means the Workshop or `scripts/hud-watch.sh` (see
[Getting it onto clients](#getting-it-onto-clients)). Building it in the pod
keeps it compiled and catches layout errors next to the C# ones; it does not put
it in front of anyone.

By hand:

```sh
./build.sh
```

Clones and builds [PanoramaCompiler](https://github.com/nicedayzhu/PanoramaCompiler),
compiles `panorama/` into `build/stage/panorama/`, copies that tree loose to
`dist/panorama/`, and packs `build/upload/<WORKSHOP_ITEM_ID>.vpk`.

**The vpk must be named after the workshop item id.** CS2 resolves an addon as
`steamapps/workshop/content/730/<id>/<id>.vpk`, so a friendly name downloads
correctly and then fails to mount, and the mount error names a path rather than
the mismatch. Set `WORKSHOP_ITEM_ID` when building anything destined for the
Workshop; it falls back to `5stack_utility_hud` only for the very first publish,
before an id exists. The CI workflow passes `item_id` through for this and
refuses to upload a vpk whose name does not match. PanoramaCompiler writes Source 2
containers directly and never invokes `resourcecompiler.exe`, so this needs only
the .NET 10 SDK — no Windows, no CS2 install.

`vpkeditcli` is fetched into `.tools/` automatically on Linux; on macOS install
it yourself or the build stops after compiling. Both `.tools/` and the
`.compiler/` clone are gitignored and excluded from the codepier sync — the
clone is ~570 MB and would otherwise be copied into the pod on every `up`.

The addon ships **no raster images**. The minimap draws on CS2's own
`s2r://panorama/images/overheadmaps/<map>_radar.psd`, because a texture compiled
by PanoramaCompiler's experimental image path spawned an entity that rendered
nothing. The web panel's radars are SimpleRadar — a drop-in replacement for
Valve's, same framing — so `radars/metadata.json` projects correctly onto either
and the two UIs still agree to the pixel. That is why the swap cost nothing, and
why `scripts/sync-radars.sh` mirrors the calibration only.

Known limit: SimpleRadar puts Nuke's and Vertigo's two levels on one image and
Valve ships them as separate `_lower_radar` files, so lower-level markers on
those two maps are offset. The other eight are exact.

Icons are SVG. One of CS2's own draws from the stylesheet as
`background-image: url("s2r://panorama/images/icons/equipment/molotov.vsvg")`
(seen in game, 2026-10-13). The extension is `.vsvg`, the compiled name without
its `_c`, which is the form the stock stylesheets use themselves; `wash-color`
tints the white art. The figures in `panorama/images/custom_game/` compile to
`.vsvg_c` and are referenced the same way, but whether an SVG of our own draws
is not yet confirmed.

A raster image needs more than the compiler gives it. Stock stores `x.png` as
`x_png.vtex_c` (every one of its 34,080 textures carries the source extension in
its name) and PanoramaCompiler writes `x.vtex_c`, so a reference to it looks for
a file that is not there. That may be all that went wrong with the earlier
attempt; it has not been retried.

The staged tree keeps its `panorama/` prefix, and the pack step refuses to
finish without it: `StrLayout` resolves `panorama/layout/custom_game/<name>.vxml_c`
against the mounted search-path root, so a vpk packed one level too deep mounts
cleanly and resolves nothing.

Pin the compiler with `PANORAMA_COMPILER_REF` once a revision is verified
against a live build. It emits a deliberately minimal resource profile; a change
in what it writes is a change in what the client parses.

## Getting it onto clients

The layouts render from resources the client must already have mounted, and
there is no reliable server-side signal that a player resolved them — the only
evidence is a click that never arrives. The plugin therefore treats the HUD as
best-effort: if the entity will not spawn, every panel falls back to the centre
text that has always worked, and `.hud` lets a player choose the text path
anyway.

- **Development** — `scripts/hud-watch.sh`, run locally once `codepier up` has
  the pod building. `build/` does not cross the sync, so `build.sh` also leaves
  the compiled tree in `dist/panorama/`; the script copies it as loose files
  into `game/csgo/panorama/` of a CS2 install (`CS2_DIR`, by default the game
  PC's share) and removes them again on exit.

  A running client keeps every layout and stylesheet it has loaded, by name,
  until it exits, so a rebuilt file under the same name needs CS2 relaunched to
  be seen. The dev loop gives each build names the client has never seen
  instead. `dev.sh` builds with `HUD_DEV_TAG` set to a timestamp; `build.sh`
  then compiles, beside the plain build, a copy in which every file under a
  `custom_game/` directory is renamed `<stem>__<tag>` and the references between
  them rewritten. `hud-watch.sh` writes the tag of the build it has finished
  copying to `dist/hud-live-tag`, `dev.sh` relays that into the plugin
  directory, and `HudKit` respawns the layouts under the tagged names, closing
  whatever panel was open. The previous build's tagged files leave the install
  as the new ones land. The sources, the names in `HudSlots.cs`, the vpk and a
  published build are never tagged. `hud-watch.sh --plain` keeps the plugin on
  the plain names. All of it rests on a running client loading a file it has
  not seen before, which it does (seen in game, 2026-10-13).

  `gameinfo.gi` is never touched: `game/csgo` is already on the stock search
  path, and an edited `gameinfo.gi` puts the client in untrusted mode. The
  script keeps a copy of the stock file beside it as `gameinfo.gi.original`
  (and in `~/.5stack/cs2-gameinfo/`) and warns if the live one stops being
  Valve's.

  **A mounted workshop addon wins over a loose file at the same path** (seen in
  game, 2026-10-13): the client drew the published `nade_hud` and stylesheet,
  while layouts that existed only loose loaded from disk and came out unstyled.
  Two things keep that from biting. Blank `HUD_WORKSHOP_ID` on the dev server:
  `setup.sh` then never links AddonsManager, so no addon is announced. It takes
  a server restart and a CS2 relaunch to show, and a client without the loose
  files then gets no HUD there. And a layout or stylesheet that changes after a
  publish takes a new name, so the loose copy is seen even on a server that
  still serves the addon; the rule sits beside the names in `HudSlots.cs`.
- **Production** — already wired. `HUD_WORKSHOP_ID` in `apps/swiftly/Dockerfile`
  holds the published id, and `setup.sh` installs
  [AddonsManager](https://github.com/SwiftlyS2-Plugins/AddonsManager) and writes
  its config on practice servers only. Set the variable empty to disable the
  mount; the plugin then falls back to centre text. It must be a bare numeric
  id — anything else is refused with a log line and treated as unset, because
  AddonsManager validates its config on start and would otherwise fail to load
  rather than degrade.

There is no way round the Workshop, and it is not a gap in SwiftlyS2. CS2 has no
server-to-client file transfer at all — no `sv_downloadurl`, no file netmessage;
the whole of `IGameFileSystem` is server-local. AddonsManager never sends anyone
a file: it mounts the vpk on the SERVER and tells connecting clients which
workshop id they need, and Steam delivers it to them. The engine natively mounts
exactly one addon — the map — which is the entire reason that plugin exists.

### Publishing

The published item is **3791548068**. Pass it as `item_id` on every run; a blank
one creates a second item rather than updating it. (3791537475 was a first
attempt under a different account and is abandoned — an item cannot be
transferred between accounts, so switching publisher means republishing.)

Valve does not review Workshop items — there is no submission and no queue. The
only Valve review in CS2 is for maps being considered for official matchmaking,
which has nothing to do with an addon a server mounts. Three things do gate it:

**The Steam Workshop Legal Agreement.** An account that has never accepted it
publishes items that exist, report `Success.`, and are invisible to everyone
including the server. Accept it once on the item page while signed in as the
publishing account. This is the first thing to check when an item uploads
cleanly and still will not download.

**Visibility must be Public.** AddonsManager fetches by id over the anonymous
workshop path, so a Private item is not downloadable by the server or by anyone
else.

**`AddonConfig/VpkDirectories` decides what the item contains.** CS2's Workshop
Manager only collects whitelisted directories. The stock `gameinfo.gi` lists
`panorama/layout/custom_game` and `panorama/styles/custom_game` (checked
2026-10-13) and nothing else under `panorama/layout` or `panorama/styles`.
Publish from outside the list and you get a valid, public, *empty* item — no
error anywhere. Workshop Manager's contents preview must list `[vxml_c]` and
`[vcss_c]` before you submit.

### Publishing from CI

The addon is published with the plugin that needs it. The practice plugin ships
in the SwiftlyS2 image, so the CI run that releases that image (a push to `main`
or `beta` that changes it) also runs **Publish HUD Addon**, which checks the
slot contract, builds the addon, refuses to upload one that is missing a layout
or the `panorama/` prefix, and updates the item `apps/swiftly/Dockerfile` mounts
(`HUD_WORKSHOP_ID`). Unchanged sources are skipped, so most releases publish
nothing.

There is one Workshop item for every server on both channels, so a release
reaches players on servers still running an older plugin. That is the reason a
published layout or stylesheet is never edited in place: a change takes a new
name, so a publish only ever adds files the older plugin does not ask for. The
rule is beside the names in `HudSlots.cs`.

It can still be run by hand from the Actions tab. `dry_run` does everything
except the upload. By hand, a blank `item_id` creates a new item and logs its
id; pass an id to update that item instead.

**An unchanged addon is not republished.** A Workshop update notifies every
subscriber and bumps the item, so the run first hashes everything that decides
the published bytes — the `panorama/` tree, `build.sh`, `addoninfo.txt`, the
preview, the compiler ref — and skips the upload if a `hud-published/<key>` tag
says those exact sources already went up. The tag is written only after Steam
answers `Success.`, so a failed upload never suppresses the retry.

It hashes sources rather than the vpk because nothing guarantees PanoramaCompiler
is byte-reproducible, and a compiler that stamped a timestamp would defeat an
artifact hash silently — always republishing, which is the failure you would not
notice.

Two things it deliberately does not cover: the item title and description live in
the workflow, not in the hashed sources, so changing those needs `force: true`.
And a dry run always builds, since verifying an unchanged addon is the point of
one.

The storefront image is `preview.jpg` (or `preview.png`; jpg wins if both
exist). Steam caps it at 1 MB, which a full-resolution PNG screenshot exceeds —
`preview.jpg` is the same frame at quality 80, 1915x1078 and 453 KB. Both
publish paths check the size first, because Steam reports an oversized preview
as a generic upload failure that never mentions the image.

`STEAM_USERNAME` names the account that owns the item, and how it authenticates
depends on whether that account has a guard:

- no Steam Guard — set `STEAM_PASSWORD`;
- Steam Guard on — set `STEAM_CONFIG_VDF`, below.

Set both and the session wins. Use an account that owns nothing else you care
about either way: these credentials upload Workshop content as that user, and a
secret in CI lives on a machine you do not watch.

### A publishing account with Steam Guard

`STEAM_PASSWORD` only works on an account with no guard; with one, SteamCMD sits
waiting for a code no workflow can answer. Set **`STEAM_CONFIG_VDF`** instead —
base64 of a `config.vdf` from a session that already answered the prompt. The
workflow prefers it over the password whenever it is set, and the password
secret can then be deleted.

Do **not** use the `steamcmd/steamcmd` Docker image on an Apple Silicon Mac. Its
SteamCMD is a 32-bit Linux binary, QEMU cannot run it, and Rosetta does not do
32-bit either, so it dies on `futex robust_list not initialized by pthreads`.
The macOS build is 64-bit x86 and runs fine under Rosetta:

```sh
mkdir -p ~/steamcmd && cd ~/steamcmd
curl -sSL https://steamcdn-a.akamaihd.net/client/installer/steamcmd_osx.tar.gz | tar zx
./steamcmd.sh +login <account> +quit     # password, then the guard code
base64 -i ~/Library/Application\ Support/Steam/config/config.vdf | pbcopy
```

Stop if the login does not reach `Waiting for user info...OK` — a failed login
writes no session, and the `config.vdf` copied after one authorises nothing.

That file is a bearer credential: whoever holds it is that account for Workshop
purposes. Steam also expires it, so expect to repeat this occasionally. The job
fails naming the cause rather than hanging when it does.

### Publishing by hand

`publish.sh` does the same thing locally and needs SteamCMD installed. The CI
route is preferred — it cannot skip the contract check, and it cannot publish
from a working tree with uncommitted layout edits in it.

That collection step is also Windows-only. `publish.sh` skips both by uploading
the packed vpk straight through SteamCMD:

```sh
STEAM_USER=<account> ./publish.sh          # first publish, prints an item id
WORKSHOP_ITEM_ID=<id> STEAM_USER=<account> ./publish.sh   # updates in place
```

It needs a preview image beside it and refuses to publish a content folder with
no vpk in it — the empty-item failure has no other symptom.

App 730 does accept a SteamCMD workshop upload — verified on 2026-08-28, item
3791537475, created and committed from an Ubuntu runner. Workshop Manager and
its `VpkDirectories` whitelist are therefore avoidable entirely, which is the
whole reason this path exists.

Practice servers first, which is why the install sits inside the
`INSTALL_UTILITY_PRACTICE_PLUGIN` branch of `setup.sh` rather than beside the
match plugin. A one-time addon download is a fair price for someone who typed
`.drill`; it is not something to ask ten people for before a match.

Verify a published item is actually downloadable before relying on it — this is
exactly what a joining client does, and it fails for reasons the Workshop page
does not show:

```sh
steamcmd +login anonymous +workshop_download_item 730 3791548068 +quit
```

A newly published item returns `Access Denied` for some minutes until Steam
finishes processing it, while the API already reports `result: 1` and
`visibility: 0`. That gap is not an error and not a review queue; it just
resolves on its own.
