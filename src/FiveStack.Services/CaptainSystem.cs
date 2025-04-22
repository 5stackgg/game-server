using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public class CaptainSystem
{
    private readonly MatchEvents _matchEvents;
    private readonly GameServer _gameServer;
    private readonly MatchService _matchService;
    private readonly ILogger<CaptainSystem> _logger;

    public Dictionary<CsTeam, CCSPlayerController?> _captains = new Dictionary<
        CsTeam,
        CCSPlayerController?
    >
    {
        { CsTeam.Terrorist, null },
        { CsTeam.CounterTerrorist, null },
    };

    public CaptainSystem(
        ILogger<CaptainSystem> logger,
        MatchEvents matchEvents,
        GameServer gameServer,
        MatchService matchService
    )
    {
        _logger = logger;
        _matchEvents = matchEvents;
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
        CsTeam team = player.Team;

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

        _matchEvents.PublishGameEvent(
            "captain",
            new Dictionary<string, object>
            {
                { "claim", false },
                { "steam_id", player.SteamID.ToString() },
                { "player_name", player.PlayerName },
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
        MatchData? matchData = _matchService.GetCurrentMatch()?.GetMatchData();

        if (matchData != null && matchData.options.type == "Duel")
        {
            return;
        }

        foreach (var pair in _captains)
        {
            CsTeam? team = pair.Key;

            if (pair.Value == null)
            {
                _gameServer.Message(
                    HudDestination.Notify,
                    $"[{TeamUtility.TeamNumToString((int)team)}] {ChatColors.Green}{CommandUtility.SilentChatTrigger}captain to claim"
                );
                continue;
            }

            _gameServer.Message(
                HudDestination.Notify,
                $"[{TeamUtility.TeamNumToString((int)team)} Captain] {(team == CsTeam.Terrorist ? ChatColors.Gold : ChatColors.Blue)}{pair.Value.PlayerName}"
            );
        }
    }

    public void ClaimCaptain(CCSPlayerController player, CsTeam team)
    {
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

            _matchEvents.PublishGameEvent(
                "captain",
                new Dictionary<string, object>
                {
                    { "claim", true },
                    { "steam_id", player.SteamID.ToString() },
                    { "player_name", player.PlayerName },
                }
            );
        }

        ShowCaptains();
    }

    public bool IsCaptain(CCSPlayerController player, CsTeam team)
    {
        if (player.IsBot)
        {
            return false;
        }

        if (team != CsTeam.Terrorist && team != CsTeam.CounterTerrorist)
        {
            return false;
        }

        if (player.Team != team)
        {
            return false;
        }

        MatchData? matchData = _matchService.GetCurrentMatch()?.GetMatchData();
        if (matchData != null)
        {
            MatchMember? member = MatchUtility.GetMemberFromLineup(matchData, player);

            if (member?.captain == true && _captains[team] == null)
            {
                _captains[team] = player;
                ShowCaptains();
            }
        }

        return _captains[team]?.SteamID == player.SteamID;
    }

    private void AutoSelectCaptain(CsTeam team)
    {
        var players = MatchUtility
            .Players()
            .FindAll(player =>
            {
                return player.Team == team && player.IsBot == false;
            });

        if (players.Count == 0)
        {
            return;
        }

        CCSPlayerController player = players[Random.Shared.Next(players.Count)];

        ClaimCaptain(player, player.Team);
    }
}
