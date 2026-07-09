# 5stack Game Server

View the documentation [docs.5stack.gg](https://docs.5stack.gg)

This repo holds the CS2 game server images and the tooling that keeps them honest.

| App | What it is | Image |
| --- | --- | --- |
| [`apps/counterstrikesharp`](apps/counterstrikesharp) | CS2 server + 5stack plugin on CounterStrikeSharp | `ghcr.io/5stackgg/game-server-css` |
| [`apps/swiftly`](apps/swiftly) | the same plugin on SwiftlyS2 | `ghcr.io/5stackgg/game-server-sw` |
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
