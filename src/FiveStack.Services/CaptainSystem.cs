using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public class CaptainSystem
{
    private readonly MatchEvents _gameEvents;
    private readonly GameServer _gameServer;
    private readonly MatchService _matchService;
    private readonly ILogger<CaptainSystem> _logger;

    public Dictionary<CsTeam, CCSPlayerController?> _captains = new Dictionary<
        CsTeam,
        CCSPlayerController?
    >
    {
        { CsTeam.Terrorist, null },
        { CsTeam.CounterTerrorist, null }
    };

    public CaptainSystem(
        ILogger<CaptainSystem> logger,
        MatchEvents gameEvents,
        GameServer gameServer,
        MatchService matchService
    )
    {
        _logger = logger;
        _gameEvents = gameEvents;
        _gameServer = gameServer;
        _matchService = matchService;
    }

    public void AutoSelectCaptains()
    {
        if (_captains[CsTeam.Terrorist] == null)
        {
            AutoSelectCaptain(CsTeam.Terrorist);
        }

        if (_captains[CsTeam.CounterTerrorist] == null)
        {
            AutoSelectCaptain(CsTeam.CounterTerrorist);
        }
    }

    public void RemoveCaptain(CCSPlayerController player)
    {
        CsTeam team = TeamUtility.TeamNumToCSTeam(player.TeamNum);

        MatchManager? match = _matchService.GetCurrentMatch();

        if (
            team == CsTeam.None
            || team == CsTeam.Spectator
            || _captains[team] != player
            || match == null
            || !match.IsWarmup()
        )
        {
            return;
        }

        _captains[team] = null;

        _gameEvents.PublishGameEvent(
            "captain",
            new Dictionary<string, object>
            {
                { "claim", false },
                { "steam_id", player.SteamID.ToString() },
                { "player_name", player.PlayerName }
            }
        );

        ShowCaptains();
    }

    public CCSPlayerController? GetTeamCaptain(CsTeam team)
    {
        return _captains[team];
    }

    public void ShowCaptains()
    {
        foreach (var pair in _captains)
        {
            CsTeam? team = pair.Key;

            if (pair.Value == null)
            {
                _gameServer.Message(
                    HudDestination.Notify,
                    $"[{TeamUtility.TeamNumToString((int)team)}] {ChatColors.Green}.captain to claim"
                );
                continue;
            }

            _gameServer.Message(
                HudDestination.Notify,
                $"[{TeamUtility.TeamNumToString((int)team)} Captain] {(team == CsTeam.Terrorist ? ChatColors.Gold : ChatColors.Blue)}{pair.Value.PlayerName}"
            );
        }
    }

    public void ClaimCaptain(CCSPlayerController player)
    {
        CsTeam team = TeamUtility.TeamNumToCSTeam(player.TeamNum);
        MatchManager? match = _matchService.GetCurrentMatch();

        if (
            team == CsTeam.None
            || team == CsTeam.Spectator
            || match == null
            || (match.IsWarmup() == false && match.IsKnife() == false)
        )
        {
            return;
        }

        if (_captains[team] == null)
        {
            _captains[team] = player;

            _gameServer.Message(
                HudDestination.Alert,
                $"{player.PlayerName} was assigned captain for the {TeamUtility.TeamNumToString((int)team)}"
            );

            _gameEvents.PublishGameEvent(
                "captain",
                new Dictionary<string, object>
                {
                    { "claim", true },
                    { "steam_id", player.SteamID.ToString() },
                    { "player_name", player.PlayerName }
                }
            );
        }

        ShowCaptains();
    }

    public bool IsCaptain(CCSPlayerController player, CsTeam? team = null)
    {
        if (team != null)
        {
            return _captains[team ?? CsTeam.None]?.SteamID == player.SteamID;
        }

        MatchData? matchData = _matchService.GetCurrentMatch()?.GetMatchData();
        ;

        if (matchData != null)
        {
            MatchMember? member = MatchUtility.GetMemberFromLineup(matchData, player);
            _logger.LogInformation("I AM CAP!");
            if (member?.captain == true)
            {
                ClaimCaptain(player);
            }
        }

        return _captains[CsTeam.CounterTerrorist]?.SteamID == player.SteamID
            || _captains[CsTeam.Terrorist]?.SteamID == player.SteamID;
    }

    private void AutoSelectCaptain(CsTeam team)
    {
        List<CCSPlayerController> players = CounterStrikeSharp
            .API.Utilities.GetPlayers()
            .FindAll(player =>
            {
                return player.TeamNum == (int)team && player.SteamID != 0;
            });

        if (players.Count == 0)
        {
            return;
        }

        CCSPlayerController player = players[Random.Shared.Next(players.Count)];

        ClaimCaptain(player);
    }
}
