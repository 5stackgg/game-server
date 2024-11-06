using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace FiveStack;

public class SurrenderSystem
{
    private readonly GameServer _gameServer;
    private readonly MatchEvents _matchEvents;
    private readonly MatchService _matchService;
    private readonly TimeoutSystem _timeoutSystem;
    private readonly ILogger<ReadySystem> _logger;

    private Dictionary<CsTeam, Timer?> _surrenderTimers = new Dictionary<CsTeam, Timer?>();
    private Dictionary<CsTeam, Timer?> _surrenderMessageTimers = new Dictionary<CsTeam, Timer?>();
    private Dictionary<CsTeam, Dictionary<ulong, bool>> _surrenderVotes =
        new Dictionary<CsTeam, Dictionary<ulong, bool>>();
    private Dictionary<CsTeam, Dictionary<ulong, Timer>> _disconnectTimers =
        new Dictionary<CsTeam, Dictionary<ulong, Timer>>();

    public SurrenderSystem(
        ILogger<ReadySystem> logger,
        GameServer gameServer,
        MatchEvents matchEvents,
        MatchService matchService,
        TimeoutSystem timeoutSystem
    )
    {
        _logger = logger;
        _gameServer = gameServer;
        _matchEvents = matchEvents;
        _matchService = matchService;
        _timeoutSystem = timeoutSystem;
        ResetTeamSurrender(CsTeam.Terrorist);
        ResetTeamSurrender(CsTeam.CounterTerrorist);
    }

    public void SetupDisconnectTimer(CsTeam team, ulong steamId)
    {
        if (_matchService.GetCurrentMatch()?.IsLive() == true)
        {
            _disconnectTimers[team][steamId] = TimerUtility.AddTimer(
                60 * 3,
                () =>
                {
                    SetupSurrender(team);
                    PlayerAbandonedMatch(steamId);
                },
                TimerFlags.REPEAT
            );
        }
    }

    public void ResetDisconnectTimers()
    {
        foreach (var team in _disconnectTimers.Keys)
        {
            foreach (var steamId in _disconnectTimers[team].Keys.ToList())
            {
                if (_disconnectTimers[team][steamId] != null)
                {
                    _disconnectTimers[team][steamId].Kill();
                }
            }
            _disconnectTimers[team].Clear();
        }
    }

    // we dont pass the team in because they may not be on the team immediately after reconnecting
    public void CancelDisconnectTimer(ulong steamId)
    {
        foreach (var _team in MatchUtility.Teams())
        {
            CsTeam team = TeamUtility.TeamNumToCSTeam(_team.TeamNum);

            if (_disconnectTimers.ContainsKey(team))
            {
                if (_disconnectTimers[team].ContainsKey(steamId))
                {
                    _disconnectTimers[team][steamId].Kill();
                    _disconnectTimers[team].Remove(steamId);
                }
            }
        }

        if (
            _disconnectTimers[CsTeam.Terrorist].Count == 0
            && _disconnectTimers[CsTeam.CounterTerrorist].Count == 0
        )
        {
            _matchService.GetCurrentMatch()?.ResumeMatch();
        }
    }

    public void SetupSurrender(CsTeam team)
    {
        if (_surrenderTimers[team] == null)
        {
            SendSurrenderMessage(team);
            _surrenderMessageTimers[team] = TimerUtility.AddTimer(
                3,
                () => SendSurrenderMessage(team),
                TimerFlags.REPEAT
            );
            _surrenderTimers[team] = TimerUtility.AddTimer(
                30,
                () =>
                {
                    CheckVotes(team, true);
                },
                TimerFlags.REPEAT
            );
        }
    }

    public bool IsSurrendering()
    {
        return _surrenderTimers[CsTeam.Terrorist] != null
            || _surrenderTimers[CsTeam.CounterTerrorist] != null;
    }

    public void CheckVotes(CsTeam team, bool force = false)
    {
        if (_surrenderVotes[team].Count != ExpectedVoteCount(team) && !force)
        {
            return;
        }

        if (_surrenderVotes[team].Count(vote => vote.Value == true) == ExpectedVoteCount(team))
        {
            Surrender(team);
            ResetTeamSurrender(CsTeam.Terrorist);
            ResetTeamSurrender(CsTeam.CounterTerrorist);
            _gameServer.Message(HudDestination.Alert, $" {ChatColors.Red}Surrender Vote Passed");
            return;
        }

        ResetTeamSurrender(team);
        _gameServer.Message(HudDestination.Alert, $" {ChatColors.Red}Surrender Vote Failed");
    }

    private void ResetTeamSurrender(CsTeam team)
    {
        if (_surrenderTimers.ContainsKey(team) && _surrenderTimers[team] != null)
        {
            _surrenderTimers[team]?.Kill();
        }

        if (_surrenderMessageTimers.ContainsKey(team) && _surrenderMessageTimers[team] != null)
        {
            _surrenderMessageTimers[team]?.Kill();
        }

        if (_disconnectTimers.ContainsKey(team) && _disconnectTimers[team] != null)
        {
            foreach (var timer in _disconnectTimers[team].Values)
            {
                timer.Kill();
            }
        }

        _surrenderTimers[team] = null;
        _surrenderMessageTimers[team] = null;
        _surrenderVotes[team] = new Dictionary<ulong, bool>();
        _disconnectTimers[team] = new Dictionary<ulong, Timer>();
    }

    private void SendSurrenderMessage(CsTeam team)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        if (match == null)
        {
            return;
        }

        foreach (var player in MatchUtility.Players())
        {
            if (player.IsBot || player.Team != team)
            {
                continue;
            }

            this._gameServer.Message(
                HudDestination.Alert,
                $" {ChatColors.Red}Surrender Vote {_surrenderVotes[team].Count}/{ExpectedVoteCount(team)}"
            );
        }
    }

    public void CastVote(CCSPlayerController? player, bool vote)
    {
        if (player == null)
        {
            return;
        }

        CsTeam team = player.Team;

        if (_surrenderVotes[team] == null)
        {
            return;
        }

        _surrenderVotes[team].Add(player.SteamID, vote);
        CheckVotes(team);
    }

    public void Surrender(CsTeam team)
    {
        MatchManager? match = this._matchService.GetCurrentMatch();
        if (match == null)
        {
            return;
        }

        MatchData? matchData = match.GetMatchData();
        if (matchData == null)
        {
            return;
        }

        Guid? lineup_id = null;

        foreach (var _team in MatchUtility.Teams())
        {
            if (TeamUtility.TeamNumToCSTeam(_team.TeamNum) == team)
            {
                lineup_id =
                    matchData.lineup_1.name == TeamUtility.CSTeamToString(team)
                        ? matchData.lineup_1_id
                        : matchData.lineup_2_id;
                break;
            }
        }

        if (lineup_id == null)
        {
            return;
        }

        _matchEvents.PublishGameEvent(
            "surrender",
            new Dictionary<string, object>
            {
                { "time", DateTime.Now },
                { "winning_lineup_id", lineup_id.Value },
            }
        );
    }

    private int ExpectedVoteCount(CsTeam team)
    {
        foreach (var _team in MatchUtility.Teams())
        {
            if (TeamUtility.TeamNumToCSTeam(_team.TeamNum) == team)
            {
                return _team.PlayerControllers.Count;
            }
        }

        return 0;
    }

    public void PlayerAbandonedMatch(ulong steamId)
    {
        _matchEvents.PublishGameEvent(
            "abandoned",
            new Dictionary<string, object>
            {
                { "time", DateTime.Now },
                { "steam_id", steamId.ToString() },
            }
        );
    }
}
