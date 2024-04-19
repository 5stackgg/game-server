# 5stack-mod

[5stack.gg](https://5stack.gg)

5Stack provides a matchmaking system for scrimmages, tournaments, and LANs.

## Features
- Match Modes
  - Competitive: Current Active Map Pool
  - Scrimmage: All Available Competitive Maps + Workshop Maps
  - Wingman: All Available Wingman Maps + Workshop Maps
- Best of X
- Automatic Team Assignment
- Kick Non-Registered Players
- Automatic Pause if Player Disconnects
- Ready Up System
- Captain System
  - Picks for Knife Round
  - Picks round reset
- Knife Round System
- Backup Round System
  - Download / Upload via S3 compatible API
  - Automatic Restore when server crashes
- Veto System
  - [x] Discord
  - [x] Web
  - [ ] Mod
- Demos System
  - Download via S3 compatible API
- Tactical Timeout System
- Tech Pauses
  - Permissions (players / admin)
- Overtime
- Workshop Maps
- Game Events
  - Report start of match
  - Report round results
  - Report map ended
- Player Stats
  - Damage
  - Kills
  - Deaths
  - Assists
  - Utility
  - Enemies Flashed
  - Friendlies Flashed
  - Objectives plant / defuse etc.
- Discord Match Scheduler
  - Player Picks
  - Match creation
  - Current score / status of match
- Setup to handle multiple servers natively
- Web UI at [5stack.gg](https://5stack.gg)
  - RCON
  - Permissions
  - Creation of matches
- Commands
  - Match Rules
  - Force Ready / Skip Knife
  - Pause / Resume
  - Reset Round

## Technical Details

Behind the scenes, 5Stack harnesses the power of Kubernetes to dynamically deploy CS2 match servers.
This architecture ensures low-latency for events to the database.

### Components

- [5Stack Mod](https://github.com/5stackgg/5stack-server-mod) using [CounterStrikeSharp](https://docs.cssharp.dev/).
- [5Stack Web](https://github.com/5stackgg/web)
- [5Stack API](https://github.com/5stackgg/api)
- [Hasura](https://hasura.io/)
- [Postgres](https://www.postgresql.org/)
- [Minio (s3)](https://min.io/)
- [Redis](https://redis.io)
- [TypeSense](https://typesense.org/)

