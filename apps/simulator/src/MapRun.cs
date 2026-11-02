using FiveStack.Entities;
using FiveStack.Enums;

namespace FiveStack.Simulator;

// One map from warmup to Finished, published the way MatchManager, the ready
// and knife systems and the round and game end handlers publish it.
public sealed class MapRun
{
    private const double FreezeTime = 20;
    private const double ShortFreezeTime = 10;
    private const double HalftimeDuration = 15;
    private const int Timeouts = 3;
    private const int SurrenderAfterRounds = 8;
    private const int LeavesAfterRounds = 5;
    private const double ReconnectAfter = 60;
    private const double AbandonAfter = 3 * 60;
    private const double VoteTime = 15;
    private const int TacticalTimeoutAfterRounds = 3;
    private const int TechnicalPauseAfterRounds = 7;
    private const double TimeoutLength = 30;

    private readonly SimulatedServer _server;
    private readonly MatchData _match;
    private readonly MatchMap _map;
    private readonly IPacer _pacer;
    private readonly Random _random;
    private readonly Roster _roster;
    private readonly Scenario _scenario;

    private readonly TaskCompletionSource _live = Signal();
    private readonly TaskCompletionSource _ready = Signal();
    private readonly TaskCompletionSource _skipKnife = Signal();
    private readonly TaskCompletionSource _knifeSwitch = Signal();
    private TaskCompletionSource? _resumed;
    private bool _inKnife;
    private bool _overtime;
    private bool _waitingForReady;
    private readonly HashSet<string> _absent = new();
    private Guid? _surrenderedTo;
    private bool _holding;
    private readonly Dictionary<Guid, int> _timeoutsLeft;
    private readonly HashSet<string> _offlineCameras = new();
    private TaskCompletionSource? _camerasBack;
    private readonly TaskCompletionSource<int> _restoreRound = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    private sealed record RoundState(int RoundsPlayed, int Lineup1Score, int Lineup2Score);

    public MapRun(
        SimulatedServer server,
        MatchData match,
        MatchMap map,
        IPacer pacer,
        Random random
    )
    {
        _server = server;
        _match = match;
        _map = map;
        _pacer = pacer;
        _random = random;
        _roster = Roster.From(match, random);
        _scenario = Scenario.From(match);
        _timeoutsLeft = new Dictionary<Guid, int>
        {
            [_roster.Lineup1Id] = Timeouts,
            [_roster.Lineup2Id] = Timeouts,
        };

        if (!_scenario.IsEmpty)
        {
            Log.Info($"scenario for lineup {_scenario.Subject}: {_scenario}");
        }
    }

    public Guid MapId => _map.id;
    public bool WaitingForLive => !_live.Task.IsCompleted;
    public bool WaitingForReady => _waitingForReady;
    public eMapStatus Status { get; private set; } = eMapStatus.Unknown;
    public int ConnectedPlayers { get; private set; }
    public Task Task { get; set; } = Task.CompletedTask;

    private MatchOptions Options => _match.options;

    // A scenario happens on the first map of the series.
    private bool ScenarioMap => _map.order <= 1;

    private Guid Opponent(Guid lineup) =>
        lineup == _roster.Lineup1Id ? _roster.Lineup2Id : _roster.Lineup1Id;

    private static TaskCompletionSource Signal()
    {
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public void MatchIsLive() => _live.TrySetResult();

    public void ForceReady() => _ready.TrySetResult();

    public void SkipKnife()
    {
        if (_inKnife)
        {
            _skipKnife.TrySetResult();
        }
    }

    public void ConfirmKnifeSwitch() => _knifeSwitch.TrySetResult();

    public void RestoreRound(int round) => _restoreRound.TrySetResult(round);

    // CameraSystem.UpdateState: the api sends the full set of players without a
    // working camera every time it changes.
    public void UpdateCameras(string payload)
    {
        lock (_offlineCameras)
        {
            _offlineCameras.Clear();
            foreach (
                string steamId in payload.Split(
                    [',', ' '],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                )
            )
            {
                if (ulong.TryParse(steamId, out ulong parsed) && parsed > 0)
                {
                    _offlineCameras.Add(steamId);
                }
            }

            Log.Info(
                _offlineCameras.Count == 0
                    ? "cameras: all clear"
                    : $"cameras offline: {string.Join(", ", _offlineCameras)}"
            );

            if (_offlineCameras.Count == 0)
            {
                _camerasBack?.TrySetResult();
                _camerasBack = null;
            }
        }
    }

    private bool CamerasOffline()
    {
        lock (_offlineCameras)
        {
            return Options.camera_required && _offlineCameras.Count > 0;
        }
    }

    private async Task WaitForCameras(CancellationToken token)
    {
        while (true)
        {
            Task back;
            lock (_offlineCameras)
            {
                if (!Options.camera_required || _offlineCameras.Count == 0)
                {
                    return;
                }
                _camerasBack ??= Signal();
                back = _camerasBack.Task;
            }
            await back.WaitAsync(token);
        }
    }

    public void Pause()
    {
        if (!_holding && Status is eMapStatus.Live or eMapStatus.Overtime)
        {
            _resumed ??= Signal();
            UpdateMapStatus(eMapStatus.Paused);
        }
    }

    public void Resume()
    {
        if (_holding || Status != eMapStatus.Paused)
        {
            return;
        }

        UpdateMapStatus(_overtime ? eMapStatus.Overtime : eMapStatus.Live);
        _resumed?.TrySetResult();
        _resumed = null;
    }

    public async Task Play(CancellationToken token)
    {
        RoundState? start = null;

        // ReplayMapAfterRestart: Rush restores no rounds, so a map it comes
        // back to is set aside and played again from warmup
        if (MapInPlay && RecordedRound() > 0 && !Options.CanRestoreRounds())
        {
            SetRoundsAside();
            await StartMap(token);
        }
        else if (MapInPlay && RestorableRound() > 0)
        {
            start = await HoldForRestore(RestorableRound(), token);
        }
        else
        {
            // rounds recorded but none to restore: played again from warmup
            if (MapInPlay && RecordedRound() > 0)
            {
                SetRoundsAside();
            }

            await StartMap(token);
        }

        (int lineup1Score, int lineup2Score, bool gameEnded) = await PlayRounds(token, start);

        if (gameEnded)
        {
            await EndMap(lineup1Score, lineup2Score, token);
        }
    }

    private async Task StartMap(CancellationToken token)
    {
        UpdateMapStatus(eMapStatus.Warmup);

        await _live.Task.WaitAsync(token);
        await ConnectPlayers(token);
        await WaitForReady(token);

        SelectCaptains();

        if (Options.KnifeRoundEnabled() && _map.order == Options.best_of)
        {
            UpdateMapStatus(eMapStatus.Knife);
            await KnifeRound(token);
        }

        UpdateMapStatus(eMapStatus.Live);

        _server.Emit(
            _match.id,
            _map.id,
            "techTimeout",
            new Dictionary<string, object>
            {
                { "map_id", _map.id },
                { "lineup_1_timeouts_available", Timeouts },
                { "lineup_2_timeouts_available", Timeouts },
            },
            stamped: false
        );
    }

    // RoundRestoreGateUtility (game-server#272): a round the api marked
    // deleted, as its last record, was undone by a restore.
    private bool IsUndone(int round) =>
        !string.IsNullOrEmpty(
            _map.rounds.LastOrDefault(record => record.round == round)?.deleted_at
        );

    private int RestorableRound() =>
        _map
            .rounds.Where(record =>
                !string.IsNullOrEmpty(record.backup_file) && !IsUndone(record.round)
            )
            .Select(record => record.round)
            .DefaultIfEmpty(0)
            .Max();

    private int RecordedRound() =>
        _map
            .rounds.Where(record => !IsUndone(record.round))
            .Select(record => record.round)
            .DefaultIfEmpty(0)
            .Max();

    private bool MapInPlay =>
        Enum.TryParse(_map.status, out eMapStatus status)
        && status is eMapStatus.Live or eMapStatus.Overtime or eMapStatus.Paused;

    // GameBackUpRounds (game-server#272): a server that comes back behind the
    // api holds the match, playing and publishing nothing, until every player
    // is back to vote on restoring the newest kept backup. A declined vote sets
    // the recorded rounds aside and the map carries on from 0-0; an admin's
    // restore during the hold ends it.
    private async Task<RoundState?> HoldForRestore(int offered, CancellationToken token)
    {
        _holding = true;
        UpdateMapStatus(eMapStatus.Live);

        await _live.Task.WaitAsync(token);
        await ConnectPlayers(token);

        Log.Info($"holding for a vote on restoring round {offered}");
        Task<int> admin = _restoreRound.Task;
        int round;

        if (await Task.WhenAny(admin, _pacer.Wait(VoteTime, token)) == admin)
        {
            round = await admin;
        }
        else if (_scenario.DeclineRestore)
        {
            Log.Info($"the vote to restore round {offered} was declined");
            _holding = false;
            SetRoundsAside();
            return null;
        }
        else
        {
            _server.Emit(
                _match.id,
                _map.id,
                "restoreRound",
                new Dictionary<string, object>
                {
                    { "round", offered },
                    { "match_map_id", _map.id },
                },
                stamped: false
            );
            round = await _restoreRound.Task.WaitAsync(token);
        }

        _holding = false;

        // RestoreRound loads the backup into a paused game the players resume
        BackupRound? restored = _map.rounds.LastOrDefault(record =>
            record.round == round && !string.IsNullOrEmpty(record.backup_file)
        );
        (int lineup1Score, int lineup2Score) = SimulatedBackup.Scores(restored?.backup_file ?? "");
        _overtime = round >= Options.MaxRounds();

        UpdateMapStatus(eMapStatus.Paused);
        await _pacer.Wait(FreezeTime, token);
        UpdateMapStatus(_overtime ? eMapStatus.Overtime : eMapStatus.Live);
        Log.Info($"restored round {round} at {lineup1Score}-{lineup2Score}");

        return new RoundState(round, lineup1Score, lineup2Score);
    }

    // A restore to round 0, which the api answers by setting every recorded
    // round aside.
    private void SetRoundsAside()
    {
        _server.Emit(
            _match.id,
            _map.id,
            "restoreRound",
            new Dictionary<string, object> { { "round", 0 }, { "match_map_id", _map.id } },
            stamped: false
        );
    }

    public void UpdateMapStatus(eMapStatus status, Guid? winningLineupId = null)
    {
        if (Status == status)
        {
            return;
        }

        bool finished = IsFinished(Status);

        if (
            finished
            && status
                is eMapStatus.Live
                    or eMapStatus.Overtime
                    or eMapStatus.Paused
                    or eMapStatus.Knife
                    or eMapStatus.Warmup
                    or eMapStatus.Scheduled
        )
        {
            return;
        }

        if (status == eMapStatus.UploadingDemo && (Status == eMapStatus.Unknown || finished))
        {
            return;
        }

        Log.Info($"map status {Status} -> {status}");

        if ((status == eMapStatus.Knife || status == eMapStatus.Live) && _roster.HasPlaceholders)
        {
            PublishConnectedPlayers(status);
        }

        _server.Emit(
            _match.id,
            _map.id,
            "mapStatus",
            new Dictionary<string, object>
            {
                { "status", status.ToString() },
                { "winning_lineup_id", winningLineupId?.ToString() ?? "" },
                { "match_map_id", _map.id.ToString() },
            },
            stamped: true
        );

        Status = status;
    }

    public static bool IsFinished(eMapStatus status)
    {
        return status is eMapStatus.Finished or eMapStatus.UploadingDemo or eMapStatus.Surrendered;
    }

    private Side Lineup1Side(int roundsPlayed)
    {
        return SideRules.LineupSide(Options, SideRules.FromApi(_map.lineup_1_side), roundsPlayed);
    }

    private Side SideOf(Guid lineupId, int roundsPlayed)
    {
        Side lineup1 = Lineup1Side(roundsPlayed);
        return lineupId == _roster.Lineup1Id ? lineup1 : SideRules.Opposite(lineup1);
    }

    private async Task ConnectPlayers(CancellationToken token)
    {
        var arrivals = _roster
            .All.Where(player => !(_scenario.NoShow && player.LineupId == _scenario.Subject))
            .Select(player => (At: 3 + _random.NextDouble() * 40, Player: player))
            .OrderBy(arrival => arrival.At)
            .ToList();

        double elapsed = 0;
        foreach ((double at, SimPlayer player) in arrivals)
        {
            await _pacer.Wait(at - elapsed, token);
            elapsed = at;

            ConnectedPlayers++;
            _server.Emit(
                _match.id,
                _map.id,
                "player-connected",
                new Dictionary<string, object>
                {
                    { "player_name", player.Name },
                    { "steam_id", player.SteamId },
                },
                stamped: false
            );
        }

        // CoachSystem: a coach joins their side without being one of its
        // players, so they ready nobody and play no round
        if (!Options.coaches)
        {
            return;
        }

        foreach (MatchLineUp lineup in new[] { _match.lineup_1, _match.lineup_2 })
        {
            if (string.IsNullOrEmpty(lineup.coach_steam_id))
            {
                continue;
            }

            await _pacer.Wait(2, token);
            _server.Emit(
                _match.id,
                _map.id,
                "player-connected",
                new Dictionary<string, object>
                {
                    { "player_name", $"{lineup.name} coach" },
                    { "steam_id", lineup.coach_steam_id },
                },
                stamped: false
            );
        }
    }

    // Everyone readies up on their own once they are all in; a match that has
    // to be started by an admin or a coach waits for force_ready, as it would.
    private async Task WaitForReady(CancellationToken token)
    {
        // ReadySystem: a player without a working camera can't ready up
        await WaitForCameras(token);

        bool readiesItself =
            (Options.ready_setting == "Players" && ConnectedPlayers >= _match.ExpectedPlayerCount())
            || (
                Options.ready_setting == "Captains"
                && _roster.Lineup1.Count > 0
                && _roster.Lineup2.Count > 0
            );

        if (!readiesItself)
        {
            Log.Info(
                $"waiting for force_ready ({Options.ready_setting} ready, {ConnectedPlayers} of {_match.ExpectedPlayerCount()} players)"
            );
            _waitingForReady = true;
            await _ready.Task.WaitAsync(token);
            _waitingForReady = false;
            return;
        }

        await Task.WhenAny(_pacer.Wait(10 + _random.NextDouble() * 20, token), _ready.Task);
        token.ThrowIfCancellationRequested();
    }

    private void SelectCaptains()
    {
        foreach (List<SimPlayer> lineup in new[] { _roster.Lineup1, _roster.Lineup2 })
        {
            if (lineup.Count == 0)
            {
                continue;
            }

            SimPlayer captain =
                lineup.FirstOrDefault(player => player.FlaggedCaptain)
                ?? lineup[_random.Next(lineup.Count)];

            _server.Emit(
                _match.id,
                _map.id,
                "captain",
                new Dictionary<string, object>
                {
                    { "claim", true },
                    { "steam_id", captain.SteamId },
                    { "player_name", captain.Name },
                },
                stamped: false
            );
        }
    }

    private void PublishConnectedPlayers(eMapStatus status)
    {
        _server.Emit(
            _match.id,
            _map.id,
            "players-connected",
            new Dictionary<string, object>
            {
                { "status", status.ToString() },
                {
                    "players",
                    _roster
                        .All.Select(player => new Dictionary<string, object>
                        {
                            { "steam_id", player.SteamId },
                            { "player_name", player.Name },
                            { "team", SideRules.ToApi(SideOf(player.LineupId, 0)) },
                            { "lineup_id", player.LineupId.ToString() },
                        })
                        .ToList()
                },
            },
            stamped: false
        );
    }

    private async Task KnifeRound(CancellationToken token)
    {
        _inKnife = true;

        try
        {
            if (await SkippedDuring(3 + 15 + _random.NextDouble() * 30, token))
            {
                return;
            }

            Guid winner = _random.Next(2) == 0 ? _roster.Lineup1Id : _roster.Lineup2Id;
            Log.Info($"knife round won by lineup {winner}");

            if (await SkippedDuring(5 + _random.NextDouble() * 10, token))
            {
                return;
            }

            if (_random.Next(2) == 0)
            {
                Log.Info("knife winner stays");
                return;
            }

            Log.Info("knife winner switches sides, waiting for api_knife_switch");
            _server.Emit(
                _match.id,
                _map.id,
                "switch",
                new Dictionary<string, object>(),
                stamped: false
            );

            await _knifeSwitch.Task.WaitAsync(token);

            (_map.lineup_1_side, _map.lineup_2_side) = (_map.lineup_2_side, _map.lineup_1_side);
        }
        finally
        {
            _inKnife = false;
        }
    }

    private async Task<bool> SkippedDuring(double gameSeconds, CancellationToken token)
    {
        Task finished = await Task.WhenAny(_pacer.Wait(gameSeconds, token), _skipKnife.Task);
        token.ThrowIfCancellationRequested();

        return finished == _skipKnife.Task;
    }

    private async Task<(int Lineup1, int Lineup2, bool GameEnded)> PlayRounds(
        CancellationToken token,
        RoundState? start
    )
    {
        int lineup1Score = start?.Lineup1Score ?? 0;
        int lineup2Score = start?.Lineup2Score ?? 0;
        int roundsPlayed = start?.RoundsPlayed ?? 0;
        int maxMoney = Economy.MaxMoney(Options);
        bool surrenders = _scenario.Surrender && ScenarioMap;
        bool timeouts = _scenario.Timeouts && ScenarioMap;
        bool chats = _scenario.Chat && ScenarioMap;
        bool leaves = (_scenario.Disconnect || _scenario.Abandon) && ScenarioMap;
        SimPlayer? leaver = null;
        List<Guid>? script = surrenders
            ? MapScript.Won(Options, Opponent(_scenario.Subject), _scenario.Subject, _random)
            : RoundScript();
        if (start != null && script != null && !(_scenario.Tie && ScenarioMap))
        {
            Guid winner = script[^1];
            bool lineup1Wins = winner == _roster.Lineup1Id;
            script = Enumerable
                .Repeat(winner, start.RoundsPlayed)
                .Concat(
                    MapScript.Rest(
                        Options,
                        winner,
                        Opponent(winner),
                        lineup1Wins ? lineup1Score : lineup2Score,
                        lineup1Wins ? lineup2Score : lineup1Score,
                        _random
                    )
                )
                .ToList();
        }
        var lossStreak = new Dictionary<Guid, int>
        {
            [_roster.Lineup1Id] = 0,
            [_roster.Lineup2Id] = 0,
        };
        Dictionary<string, object>? pendingScore = null;

        foreach (SimPlayer player in _roster.All)
        {
            player.Money = Economy.StartMoney;
        }

        // mp_restartgame 1
        await _pacer.Wait(1, token);

        while (true)
        {
            if (pendingScore != null)
            {
                _server.Emit(_match.id, _map.id, "score", pendingScore, stamped: true);
                pendingScore = null;
            }

            TaskCompletionSource? resumed = _resumed;
            if (resumed != null)
            {
                await resumed.Task.WaitAsync(token);
            }

            // CameraSystem: a player who lost their camera stops play, and
            // the players resume it once everyone is back on camera
            if (CamerasOffline())
            {
                UpdateMapStatus(eMapStatus.Paused);
                await WaitForCameras(token);
                await _pacer.Wait(10, token);
                UpdateMapStatus(_overtime ? eMapStatus.Overtime : eMapStatus.Live);
            }

            if (timeouts && roundsPlayed == TacticalTimeoutAfterRounds)
            {
                // TimeoutSystem.CallTacTimeout: the team spends a timeout and
                // CS2 holds the freeze time for it
                _timeoutsLeft[_scenario.Subject]--;
                _server.Emit(
                    _match.id,
                    _map.id,
                    "techTimeout",
                    new Dictionary<string, object>
                    {
                        { "map_id", _map.id },
                        { "lineup_1_timeouts_available", _timeoutsLeft[_roster.Lineup1Id] },
                        { "lineup_2_timeouts_available", _timeoutsLeft[_roster.Lineup2Id] },
                    },
                    stamped: false
                );
                await _pacer.Wait(TimeoutLength, token);
            }

            if (timeouts && roundsPlayed == TechnicalPauseAfterRounds)
            {
                // a technical pause both teams have to resume, the one that
                // called it first
                UpdateMapStatus(eMapStatus.Paused);
                await _pacer.Wait(20, token);
                Log.Info("the team that paused resumed, waiting for the other team");
                await _pacer.Wait(10, token);
                UpdateMapStatus(_overtime ? eMapStatus.Overtime : eMapStatus.Live);
            }

            if (chats && roundsPlayed == 1)
            {
                Chat(_roster.Of(_scenario.Subject)[0], "glhf");
            }

            if (chats && roundsPlayed == 2)
            {
                TeamChat(_roster.Of(_scenario.Subject)[0], "save this one");
            }

            // RoundStart.cs: a round that starts short of players is paused
            if (leaver != null && _absent.Contains(leaver.SteamId) && Status != eMapStatus.Paused)
            {
                UpdateMapStatus(eMapStatus.Paused);

                if (_scenario.Disconnect)
                {
                    await _pacer.Wait(ReconnectAfter, token);
                    _absent.Remove(leaver.SteamId);
                    Connected(leaver);
                    UpdateMapStatus(_overtime ? eMapStatus.Overtime : eMapStatus.Live);
                }
                else
                {
                    // SurrenderSystem's disconnect timer: the leaver is reported
                    // and their team votes to surrender, while the game stays
                    // paused for the player who never comes back.
                    await _pacer.Wait(AbandonAfter, token);
                    _server.Emit(
                        _match.id,
                        _map.id,
                        "abandoned",
                        new Dictionary<string, object> { { "steam_id", leaver.SteamId } },
                        stamped: true
                    );
                    await _pacer.Wait(VoteTime, token);
                    Surrender();
                    return (lineup1Score, lineup2Score, false);
                }
            }

            if (surrenders && roundsPlayed == SurrenderAfterRounds)
            {
                await _pacer.Wait(FreezeTime + 10, token);
                Surrender();

                // with nobody left on one side, CS2 gives every round to the
                // other until it has won the map
                Guid remaining = Opponent(_scenario.Subject);
                Side winnerSide = Side.None;
                while (!SideRules.IsMapOver(Options, lineup1Score, lineup2Score))
                {
                    winnerSide = SideOf(remaining, roundsPlayed);
                    await _pacer.Wait(FreezeTime + 2 + (Options.round_restart_delay ?? 7), token);
                    if (remaining == _roster.Lineup1Id)
                    {
                        lineup1Score++;
                    }
                    else
                    {
                        lineup2Score++;
                    }
                    roundsPlayed++;
                }

                Dictionary<string, object> last = Score(
                    roundsPlayed,
                    lineup1Score,
                    lineup2Score,
                    winnerSide,
                    winnerSide == Side.T ? eWinReason.TerroristsWin : eWinReason.CTsWin
                );
                last["backup_file"] = "";
                _server.Emit(_match.id, _map.id, "score", last, stamped: true);
                return (lineup1Score, lineup2Score, true);
            }

            await _pacer.Wait(
                Options.type is "Wingman" or "Duel" ? ShortFreezeTime : FreezeTime,
                token
            );

            Side lineup1Side = Lineup1Side(roundsPlayed);
            List<SimPlayer> terrorists = Present(
                lineup1Side == Side.T ? _roster.Lineup1 : _roster.Lineup2
            );
            List<SimPlayer> counterTerrorists = Present(
                lineup1Side == Side.T ? _roster.Lineup2 : _roster.Lineup1
            );
            bool pistolRound =
                roundsPlayed == 0 || (roundsPlayed == Options.mr && Options.SwapsSides());

            Economy.Buy(terrorists, Side.T, Economy.TeamTier(terrorists, pistolRound), _random);
            Economy.Buy(
                counterTerrorists,
                Side.CT,
                Economy.TeamTier(counterTerrorists, pistolRound),
                _random
            );

            RoundOutcome outcome = RoundSimulator.Play(
                new RoundSetup
                {
                    Round = roundsPlayed + 1,
                    MatchMapId = _map.id,
                    MapName = _map.map.name,
                    Terrorists = terrorists,
                    CounterTerrorists = counterTerrorists,
                },
                _random,
                script != null && roundsPlayed < script.Count
                    ? SideOf(script[roundsPlayed], roundsPlayed)
                    : null
            );

            double elapsed = 0;
            foreach (PlannedEvent planned in outcome.Events)
            {
                await _pacer.Wait(planned.At - elapsed, token);
                elapsed = planned.At;
                _server.Emit(_match.id, _map.id, planned.Name, planned.Data, stamped: true);
            }
            await _pacer.Wait(outcome.Duration - elapsed, token);

            Guid winner = outcome.Winner == lineup1Side ? _roster.Lineup1Id : _roster.Lineup2Id;
            Guid loser = winner == _roster.Lineup1Id ? _roster.Lineup2Id : _roster.Lineup1Id;
            if (winner == _roster.Lineup1Id)
            {
                lineup1Score++;
            }
            else
            {
                lineup2Score++;
            }

            lossStreak[winner] = 0;
            lossStreak[loser] = Math.Min(lossStreak[loser] + 1, 5);

            foreach (SimPlayer player in _roster.Of(winner))
            {
                player.Money += Economy.WinReward(outcome.Reason);
            }
            foreach (SimPlayer player in _roster.Of(loser))
            {
                bool plantBonus = outcome.BombPlanted && SideOf(loser, roundsPlayed) == Side.T;
                player.Money += Economy.LossBonus(lossStreak[loser]) + (plantBonus ? 800 : 0);
            }
            foreach (SimPlayer player in _roster.All)
            {
                player.Money = Math.Min(
                    maxMoney,
                    player.Money + outcome.KillRewards.GetValueOrDefault(player.SteamId)
                );
            }

            roundsPlayed++;
            Log.Info(
                $"round {roundsPlayed}: {outcome.Winner} {outcome.Reason} ({lineup1Score}-{lineup2Score})"
            );

            Dictionary<string, object> score = Score(
                roundsPlayed,
                lineup1Score,
                lineup2Score,
                outcome.Winner,
                outcome.Reason
            );

            if (SideRules.IsMapOver(Options, lineup1Score, lineup2Score))
            {
                // GameEnd.cs publishes the last round without a backup
                score["backup_file"] = "";
                _server.Emit(_match.id, _map.id, "score", score, stamped: true);

                if (chats)
                {
                    Chat(_roster.Of(Opponent(_scenario.Subject))[0], "gg");
                }
                return (lineup1Score, lineup2Score, true);
            }

            if (leaves && leaver == null && roundsPlayed == LeavesAfterRounds)
            {
                List<SimPlayer> lineup = _roster.Of(_scenario.Subject);
                leaver = lineup.FirstOrDefault(player => !player.FlaggedCaptain) ?? lineup[0];
                _absent.Add(leaver.SteamId);
                Disconnected(leaver);
            }

            await _pacer.Wait(Options.round_restart_delay ?? 7, token);

            if (SideRules.IsOvertime(Options, roundsPlayed) && !_overtime)
            {
                _overtime = true;
                UpdateMapStatus(eMapStatus.Overtime);
            }

            pendingScore = score;

            if (SideRules.ResetsMoney(Options, roundsPlayed))
            {
                int money =
                    roundsPlayed >= Options.MaxRounds()
                        ? Economy.OvertimeStartMoney
                        : Economy.StartMoney;
                foreach (SimPlayer player in _roster.All)
                {
                    player.Money = money;
                }
            }

            if (SideRules.SidesSwitch(Options, roundsPlayed))
            {
                await _pacer.Wait(HalftimeDuration, token);
            }
        }
    }

    // PlayerChat: all chat goes to the match, team chat to the player's own
    // lineup, and only when the api says it takes it.
    private void Chat(SimPlayer player, string message)
    {
        _server.Emit(
            _match.id,
            _map.id,
            "chat",
            new Dictionary<string, object> { { "player", player.SteamId }, { "message", message } },
            stamped: false
        );
    }

    private void TeamChat(SimPlayer player, string message)
    {
        if (!_match.relay_team_chat)
        {
            return;
        }

        _server.Emit(
            _match.id,
            _map.id,
            "teamChat",
            new Dictionary<string, object>
            {
                { "player", player.SteamId },
                { "message", message },
                { "lineupId", player.LineupId.ToString() },
            },
            stamped: false
        );
    }

    private List<SimPlayer> Present(List<SimPlayer> lineup)
    {
        return lineup.Where(player => !_absent.Contains(player.SteamId)).ToList();
    }

    private void Connected(SimPlayer player)
    {
        _server.Emit(
            _match.id,
            _map.id,
            "player-connected",
            new Dictionary<string, object>
            {
                { "player_name", player.Name },
                { "steam_id", player.SteamId },
            },
            stamped: false
        );
    }

    private void Disconnected(SimPlayer player)
    {
        _server.Emit(
            _match.id,
            _map.id,
            "player-disconnected",
            new Dictionary<string, object> { { "steam_id", player.SteamId } },
            stamped: false
        );
    }

    // SurrenderSystem: the vote passes, the map goes to the other lineup, and
    // the lineup that gave up leaves.
    private void Surrender()
    {
        _surrenderedTo = Opponent(_scenario.Subject);
        UpdateMapStatus(eMapStatus.Surrendered, _surrenderedTo);

        foreach (SimPlayer player in _roster.Of(_scenario.Subject))
        {
            if (_absent.Add(player.SteamId))
            {
                Disconnected(player);
            }
        }
    }

    // Who wins each round, when the scenario decides this map.
    private List<Guid>? RoundScript()
    {
        Guid Other(Guid lineup) =>
            lineup == _roster.Lineup1Id ? _roster.Lineup2Id : _roster.Lineup1Id;

        if (_scenario.Tie)
        {
            return MapScript.Tied(Options, _roster.Lineup1Id, _roster.Lineup2Id, _random);
        }

        if (_scenario.Wins == null)
        {
            return null;
        }

        List<bool>? plan = _scenario.SeriesPlan(Options.best_of);
        if (plan == null)
        {
            Log.Warn(
                $"sim:{_scenario.Wins}-{_scenario.Losses} cannot end a best of {Options.best_of}, playing it out"
            );
            return null;
        }

        if (_map.order < 1 || _map.order > plan.Count)
        {
            return null;
        }

        Guid winner = plan[_map.order - 1] ? _scenario.Subject : Other(_scenario.Subject);
        return MapScript.Won(Options, winner, Other(winner), _random);
    }

    private Dictionary<string, object> Score(
        int roundsPlayed,
        int lineup1Score,
        int lineup2Score,
        Side winner,
        eWinReason reason
    )
    {
        Side lineup1Side = Lineup1Side(roundsPlayed - 1);

        return new Dictionary<string, object>
        {
            { "match_map_id", _map.id },
            { "round", roundsPlayed },
            { "lineup_1_score", lineup1Score },
            { "lineup_1_money", _roster.Lineup1.Sum(player => player.Money).ToString() },
            { "lineup_1_timeouts_available", $"{_timeoutsLeft[_roster.Lineup1Id]}" },
            { "lineup_2_score", lineup2Score },
            { "lineup_2_money", _roster.Lineup2.Sum(player => player.Money).ToString() },
            { "lineup_2_timeouts_available", $"{_timeoutsLeft[_roster.Lineup2Id]}" },
            { "lineup_1_side", SideRules.ToApi(lineup1Side) },
            { "lineup_2_side", SideRules.ToApi(SideRules.Opposite(lineup1Side)) },
            { "winning_side", SideRules.ToApi(winner) },
            { "winning_reason", reason.ToString() },
            {
                "backup_file",
                SimulatedBackup.Write(roundsPlayed, lineup1Score, lineup2Score, _roster)
            },
        };
    }

    // GameEnd.cs: a surrender goes out first, the winner goes out with
    // WaitingForTV, the map is Finished once the tv delay has passed, and the
    // server asks for its next map. There is no demo: off a game node the
    // upload finds nothing to send.
    private async Task EndMap(int lineup1Score, int lineup2Score, CancellationToken token)
    {
        Guid winner = lineup1Score > lineup2Score ? _match.lineup_1_id : _match.lineup_2_id;

        Log.Info($"map over {lineup1Score}-{lineup2Score}");
        if (_surrenderedTo is Guid surrenderedTo)
        {
            _server.Emit(
                _match.id,
                _map.id,
                "surrender",
                new Dictionary<string, object>
                {
                    { "winning_lineup_id", surrenderedTo.ToString() },
                },
                stamped: true
            );
        }
        UpdateMapStatus(eMapStatus.WaitingForTV, winner);

        await _pacer.Wait(Options.use_playcast ? 5 : Options.tv_delay, token);

        if (!_server.Settings.OnGameNode)
        {
            UpdateMapStatus(eMapStatus.UploadingDemo, winner);
            await _pacer.Wait(15, token);
            await _server.UploadDemo(_match.id, _map.id, token);
        }

        UpdateMapStatus(eMapStatus.Finished, _surrenderedTo ?? winner);

        // delayChangeMap: nothing moves the server on until this has run out
        _server.HoldFetches = true;
        try
        {
            await _pacer.Wait(5, token);
        }
        finally
        {
            _server.HoldFetches = false;
        }

        await _server.RefreshMatch();
    }
}
