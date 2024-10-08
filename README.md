# 5stack Game Server and Plugin

[5stack.gg](https://5stack.gg)

The game server requires the setup of the 5stack Panel, to setup the panel view [documentation](https://docs.5stack.gg).

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