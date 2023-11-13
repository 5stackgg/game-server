# playcs-mod

## Features
- [x] automatic team assignment
- [x] automatic pause if player disconnects
- [x] tactical timeouts (4 30 second timeouts for each team )
  - [ ] db reporting
- [x] tech pause / unpause
- [x] Knife Round (with captains)
  - [x] stay / switch
- [x] ready system
- [x] Overtime (infinite)]
- [x] Workshop Maps
  - [ ]  adding ability by config
- [x] Game Events
    - [x] Report start of match
    - [x] Report round results
    - [x] Report map ended
- [x] Player Stats
    - [x] Damage
    - [x] Kills
    - [x] Assists
- [x] Discord Veto System
- [x] demo recording
  - [ ] downloadable
- [x] round restore

## WIP

## Future 
- [ ] coach support
- [ ] best of x
- [ ] Configuration via http(s) json / json file
- [ ] Advanced stats
  - [ ] TradeKill
  - [ ] Enemies Flash /
  - [ ] Friendlys Flashed
  - [ ] MVP
  - [ ] death by bomb / suicide
  - [ ] won by plant / defuse (and who) 
- [ ] web ui at playcs.live
- [ ] allow players .pause / .resume or just admin
- [ ] require steam id
  - [ ] kick if not assigned 
  - [ ] discord assign steam id

## Dev TODO
[ ] .env setup
[ ] subscribed map list / validate
[ ] matches at k8s jobs
[ ] mid-match server restart = bad news bears 
  [ ] need to re-ready up 
  [ ] need to load last round