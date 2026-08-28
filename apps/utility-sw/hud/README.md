# utility-practice HUD

The Panorama half of the practice plugin. `panorama/` is authored here and
compiled into a workshop addon the client mounts; the C# in `../src/Hud/` drives
it through `CCSCustomHudLayout`.

| Layout | Captures input | What it is |
| --- | --- | --- |
| `nade_hud` | no | Guidance: where it lands, how to throw it, which way to look, live aim error, drill progress. Replaces the three centre-text channels. |
| `nade_list` | yes | The picker behind `.menu`. Side and type chips, a *from here* toggle, 16 natively scrolling rows. |
| `nade_map` | yes | The minimap behind `.map`. Up to 40 markers, one per landing spot, with an on-map detail popover. |
| `nade_run` | no | The execute behind `.playbook`. Who throws what and when, yours in colour and everyone else's greyed. |
| `nade_edit` | yes | Rename, describe, change visibility. Fields ask for their value in chat. |

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
- `nade_edit` contains no geometry field, so editing where a smoke lands can
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

The addon half is **not** hot-reloadable, though — the vpk has to reach a game
*client*, which means a local `gameinfo.gi` override or the Workshop. Building it
in the pod keeps it compiled and catches layout errors next to the C# ones; it
does not put it in front of anyone.

By hand:

```sh
./build.sh
```

Clones and builds [PanoramaCompiler](https://github.com/nicedayzhu/PanoramaCompiler),
compiles `panorama/` into `build/stage/panorama/`, and packs
`build/upload/5stack_utility_hud.vpk`. PanoramaCompiler writes Source 2
containers directly and never invokes `resourcecompiler.exe`, so this needs only
the .NET 10 SDK — no Windows, no CS2 install.

`vpkeditcli` is fetched into `.tools/` automatically on Linux; on macOS install
it yourself or the build stops after compiling. Both `.tools/` and the
`.compiler/` clone are gitignored and excluded from the codepier sync — the
clone is ~570 MB and would otherwise be copied into the pod on every `up`.

The addon ships **no images**. The minimap draws on CS2's own
`s2r://panorama/images/overheadmaps/<map>_radar.psd`, because a texture compiled
by PanoramaCompiler's experimental image path spawned an entity that rendered
nothing. The web panel's radars are SimpleRadar — a drop-in replacement for
Valve's, same framing — so `radars/metadata.json` projects correctly onto either
and the two UIs still agree to the pixel. That is why the swap cost nothing, and
why `scripts/sync-radars.sh` mirrors the calibration only.

Known limit: SimpleRadar puts Nuke's and Vertigo's two levels on one image and
Valve ships them as separate `_lower_radar` files, so lower-level markers on
those two maps are offset. The other eight are exact.

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

- **Development** — point CS2 at `build/` as a local override via `gameinfo.gi`.
- **Production** — publish `build/` to the Steam Workshop and name its id in
  [AddonsManager](https://github.com/SwiftlyS2-Plugins/AddonsManager).

There is no way round the Workshop, and it is not a gap in SwiftlyS2. CS2 has no
server-to-client file transfer at all — no `sv_downloadurl`, no file netmessage;
the whole of `IGameFileSystem` is server-local. AddonsManager never sends anyone
a file: it mounts the vpk on the SERVER and tells connecting clients which
workshop id they need, and Steam delivers it to them. The engine natively mounts
exactly one addon — the map — which is the entire reason that plugin exists.

### Publishing

Workshop items go live at whatever visibility you set — there is no review queue
to wait on. Two things do gate it:

**Visibility must be Public.** AddonsManager fetches by id over the anonymous
workshop path, so a Private item is not downloadable by the server or by anyone
else.

**`AddonConfig/VpkDirectories` decides what the item contains.** CS2's Workshop
Manager only collects whitelisted directories, and neither
`panorama/layout/custom_game` nor `panorama/styles/custom_game` is in the default
list. Publish without adding them and you get a valid, public, *empty* item — no
error anywhere. Workshop Manager's contents preview must list `[vxml_c]` and
`[vcss_c]` before you submit.

### Publishing from CI

`.github/workflows/hud-workshop.yaml` is the reliable route: run **Publish HUD
Addon** from the Actions tab. It runs the slot contract, builds the addon,
refuses to upload one that is missing a layout or the `panorama/` prefix, and
publishes. `dry_run` does everything except the upload.

Leave `item_id` blank the first time; the run logs the id it created. Pass that
id on every run afterwards or you will publish a second item rather than update
the first.

Two secrets:

- `STEAM_USERNAME` — the account that owns the item.
- `STEAM_PASSWORD` — its password.

That is enough because the publishing account has Steam Guard off. Use an
account that owns nothing else you care about: these credentials can upload
Workshop content as that user, and a password in CI is a password on a machine
you do not watch.

If Steam ever does demand a guard code — it can when a login arrives from an
unfamiliar address, and runner addresses change every run — the job fails with a
clear message rather than hanging. The way round it is a pre-authorised session
instead of a password: log in once by hand on any machine with SteamCMD, then
hand the runner the resulting `config.vdf`.

```sh
steamcmd +login <account> +quit      # answer the prompt once
base64 -i ~/Steam/config/config.vdf | pbcopy
```

Restore it to `~/Steam/config/config.vdf` on the runner before the publish step
and drop the password from the login line.

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

It needs a `preview.png` beside it and refuses to publish a content folder with
no vpk in it — the empty-item failure has no other symptom. Whether app 730
accepts a SteamCMD workshop upload at all is unverified; if it does not, fall
back to Workshop Manager and read its contents preview before submitting.

Practice servers first. A one-time addon download is a fair price for someone
who typed `.drill`; it is not something to ask ten people for before a match.
