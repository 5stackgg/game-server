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
    }

    public Guid MapId => _map.id;
    public bool WaitingForLive => !_live.Task.IsCompleted;
    public eMapStatus Status { get; private set; } = eMapStatus.Unknown;
    public int ConnectedPlayers { get; private set; }
    public Task Task { get; set; } = Task.CompletedTask;

    private MatchOptions Options => _match.options;

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

    public void Pause()
    {
        if (Status is eMapStatus.Live or eMapStatus.Overtime)
        {
            _resumed ??= Signal();
            UpdateMapStatus(eMapStatus.Paused);
        }
    }

    public void Resume()
    {
        if (Status != eMapStatus.Paused)
        {
            return;
        }

        UpdateMapStatus(_overtime ? eMapStatus.Overtime : eMapStatus.Live);
        _resumed?.TrySetResult();
        _resumed = null;
    }

    public async Task Play(CancellationToken token)
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

        (int lineup1Score, int lineup2Score) = await PlayRounds(token);

        await EndMap(lineup1Score, lineup2Score, token);
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
            },
            stamped: false
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
            .All.Select(player => (At: 3 + _random.NextDouble() * 40, Player: player))
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
    }

    // Everyone readies up on their own once they are all in; a match that has
    // to be started by an admin or a coach waits for force_ready, as it would.
    private async Task WaitForReady(CancellationToken token)
    {
        bool readiesItself =
            (
                Options.ready_setting == "Players"
                && ConnectedPlayers >= Options.ExpectedPlayerCount()
            )
            || (
                Options.ready_setting == "Captains"
                && _roster.Lineup1.Count > 0
                && _roster.Lineup2.Count > 0
            );

        if (!readiesItself)
        {
            Log.Info(
                $"waiting for force_ready ({Options.ready_setting} ready, {ConnectedPlayers} of {Options.ExpectedPlayerCount()} players)"
            );
            await _ready.Task.WaitAsync(token);
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

    private async Task<(int Lineup1, int Lineup2)> PlayRounds(CancellationToken token)
    {
        int lineup1Score = 0;
        int lineup2Score = 0;
        int roundsPlayed = 0;
        int maxMoney = Economy.MaxMoney(Options);
        List<Guid>? script = RoundScript();
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

            await _pacer.Wait(
                Options.type is "Wingman" or "Duel" ? ShortFreezeTime : FreezeTime,
                token
            );

            Side lineup1Side = Lineup1Side(roundsPlayed);
            List<SimPlayer> terrorists = lineup1Side == Side.T ? _roster.Lineup1 : _roster.Lineup2;
            List<SimPlayer> counterTerrorists =
                lineup1Side == Side.T ? _roster.Lineup2 : _roster.Lineup1;
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
                outcome
            );

            if (SideRules.IsMapOver(Options, lineup1Score, lineup2Score))
            {
                _server.Emit(_match.id, _map.id, "score", score, stamped: true);
                return (lineup1Score, lineup2Score);
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
        RoundOutcome outcome
    )
    {
        Side lineup1Side = Lineup1Side(roundsPlayed - 1);

        return new Dictionary<string, object>
        {
            { "match_map_id", _map.id },
            { "round", roundsPlayed },
            { "lineup_1_score", lineup1Score },
            { "lineup_1_money", _roster.Lineup1.Sum(player => player.Money).ToString() },
            { "lineup_1_timeouts_available", $"{Timeouts}" },
            { "lineup_2_score", lineup2Score },
            { "lineup_2_money", _roster.Lineup2.Sum(player => player.Money).ToString() },
            { "lineup_2_timeouts_available", $"{Timeouts}" },
            { "lineup_1_side", SideRules.ToApi(lineup1Side) },
            { "lineup_2_side", SideRules.ToApi(SideRules.Opposite(lineup1Side)) },
            { "winning_side", SideRules.ToApi(outcome.Winner) },
            { "winning_reason", outcome.Reason.ToString() },
            // CS2 writes the backup round file; there is none to send here
            { "backup_file", "" },
        };
    }

    // GameEnd.cs: the winner goes out with WaitingForTV, the map is Finished
    // once the tv delay has passed, and the server asks for its next map.
    // There is no demo: off a game node the upload finds nothing to send.
    private async Task EndMap(int lineup1Score, int lineup2Score, CancellationToken token)
    {
        Guid winner = lineup1Score > lineup2Score ? _match.lineup_1_id : _match.lineup_2_id;

        Log.Info($"map over {lineup1Score}-{lineup2Score}");
        UpdateMapStatus(eMapStatus.WaitingForTV, winner);

        await _pacer.Wait(Options.use_playcast ? 5 : Options.tv_delay, token);

        if (!_server.Settings.OnGameNode)
        {
            UpdateMapStatus(eMapStatus.UploadingDemo, winner);
            await _pacer.Wait(15, token);
            Log.Info("no demo was recorded, nothing to upload");
        }

        UpdateMapStatus(eMapStatus.Finished, winner);

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
