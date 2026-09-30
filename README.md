# 5stack Game Server

View the documentation [docs.5stack.gg](https://docs.5stack.gg)

This repo holds the CS2 game server images and the tooling that keeps them honest.

| App | What it is | Image |
| --- | --- | --- |
| [`apps/counterstrikesharp`](apps/counterstrikesharp) | CS2 server + 5stack plugin on CounterStrikeSharp | `ghcr.io/5stackgg/game-server-css` |
| [`apps/swiftly`](apps/swiftly) | the same plugin on SwiftlyS2 | `ghcr.io/5stackgg/game-server-sw` |
| [`apps/player-management-css`](apps/player-management-css) / [`apps/player-management-sw`](apps/player-management-sw) | enforces panel bans, mutes and gags on community servers | ships inside both game-server images |
| [`apps/gamedata-validator`](apps/gamedata-validator) | checks our byte-pattern signatures still resolve after a CS2 update | `ghcr.io/5stackgg/gamedata-validator` |

`shared/` holds what the two plugins have in common: the server `cfg/`, the setup `scripts/`,
the signature `gamedata/`, and the framework-agnostic C# (entities, enums, utilities) that both
`FiveStack.csproj` files pull in with a `Compile` glob. It compiles into each plugin assembly —
nothing extra ships in the plugin zip.

# Plugin

Both plugins are published to [releases](https://github.com/5stackgg/game-server/releases). They
version independently and share one tag namespace, so pick by prefix:

- CounterStrikeSharp — `css-v0.0.N`, asset `FiveStack-css-v0.0.N.zip`
- SwiftlyS2 — `sw-v0.0.N`, asset `FiveStack-sw-v0.0.N.zip`

# Player Management

Community (non-Ranked) dedicated servers run no match plugin, so the sanctions a moderator sets in
the panel reach them through this plugin instead. It syncs every 30 seconds and whenever the panel
sends `player_management_refresh`, kicks banned players, mutes voice and blocks chat. It has no
byte-pattern signatures, so a CS2 update does not break it.

A server on a 5stack node loads it automatically. Anywhere else, take the asset from the same
release as the match plugin and extract it into `game/csgo`:

- CounterStrikeSharp: `PlayerManagement-css-v0.0.N.zip`, configured in
  `addons/counterstrikesharp/configs/plugins/PlayerManagement/PlayerManagement.json`
- SwiftlyS2: `PlayerManagement-sw-v0.0.N.zip`, configured in
  `addons/swiftlys2/configs/plugins/PlayerManagement/config.jsonc` (under a `PlayerManagement` key)

Either config takes `API_DOMAIN`, `SERVER_ID` and `SERVER_API_PASSWORD`; the panel's player
management card shows the values for each server. Environment variables of the same names win over
the file. Over RCON, `player_management_status` reports what the plugin sees.
