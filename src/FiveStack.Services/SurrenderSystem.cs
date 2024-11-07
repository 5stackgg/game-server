using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using FiveStack.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace FiveStack;

public class SurrenderSystem
{
    private readonly GameServer _gameServer;
    private readonly MatchEvents _matchEvents;
    private readonly MatchService _matchService;
    private readonly ILogger<ReadySystem> _logger;
    private readonly IServiceProvider _serviceProvider;
    public VoteSystem? surrenderingVote;

    private Dictionary<CsTeam, Dictionary<ulong, Timer>> _disconnectTimers =
        new Dictionary<CsTeam, Dictionary<ulong, Timer>>();

    public SurrenderSystem(
        ILogger<ReadySystem> logger,
        MatchEvents matchEvents,
        MatchService matchService,
        GameServer gameServer,
        IServiceProvider serviceProvider
    )
    {
        _logger = logger;
        _gameServer = gameServer;
        _matchEvents = matchEvents;
        _matchService = matchService;
        _serviceProvider = serviceProvider;
        ResetTeamSurrender(CsTeam.Terrorist);
        ResetTeamSurrender(CsTeam.CounterTerrorist);
    }

    private void ResetTeamSurrender(CsTeam team)
    {
        _disconnectTimers[team] = new Dictionary<ulong, Timer>();
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
            && _matchService.GetCurrentMatch()?.IsPaused() == true
        )
        {
            _matchService.GetCurrentMatch()?.ResumeMatch();
        }
    }

    public void SetupSurrender(CsTeam team, CCSPlayerController? player = null)
    {
        _logger.LogInformation($"Setting up surrender vote for {team}");
        if (surrenderingVote != null)
        {
            player?.PrintToConsole(" A surrender vote is already in progress");
            return;
        }

        surrenderingVote = _serviceProvider.GetRequiredService(typeof(VoteSystem)) as VoteSystem;

        if (surrenderingVote == null)
        {
            return;
        }

        _logger.LogInformation($"Starting Surrender Vote for {team}");
        surrenderingVote.StartVote(
            "Surrender",
            new CsTeam[] { team },
            () =>
            {
                Surrender(team);
                surrenderingVote = null;
                ResetTeamSurrender(CsTeam.Terrorist);
                ResetTeamSurrender(CsTeam.CounterTerrorist);
            },
            () =>
            {
                surrenderingVote = null;
                ResetTeamSurrender(CsTeam.Terrorist);
                ResetTeamSurrender(CsTeam.CounterTerrorist);
            },
            false,
            30
        );
    }

    public bool IsSurrendering()
    {
        return surrenderingVote != null;
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
            _logger.LogWarning($"No lineup id found for {team}");
            return;
        }

        _logger.LogInformation($"Surrendering ${team}:{lineup_id.Value}");

        _matchEvents.PublishGameEvent(
            "surrender",
            new Dictionary<string, object>
            {
                { "time", DateTime.Now },
                { "winning_lineup_id", lineup_id.Value },
            }
        );
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
