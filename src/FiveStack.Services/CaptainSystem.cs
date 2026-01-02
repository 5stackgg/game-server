using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using FiveStack.Utilities;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public class CaptainSystem
{
    private readonly MatchEvents _matchEvents;
    private readonly GameServer _gameServer;
    private readonly MatchService _matchService;
    private readonly ILogger<CaptainSystem> _logger;
    private readonly IStringLocalizer _localizer;

    public Dictionary<CsTeam, string?> _captains = new Dictionary<CsTeam, string?>
    {
        { CsTeam.Terrorist, null },
        { CsTeam.CounterTerrorist, null },
    };

    public CaptainSystem(
        ILogger<CaptainSystem> logger,
        MatchEvents matchEvents,
        GameServer gameServer,
        MatchService matchService,
        IStringLocalizer localizer
    )
    {
        _logger = logger;
        _matchEvents = matchEvents;
        _gameServer = gameServer;
        _matchService = matchService;
        _localizer = localizer;
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

    public Dictionary<CsTeam, CCSPlayerController?> GetCaptains()
    {
        return new Dictionary<CsTeam, CCSPlayerController?>
        {
            { CsTeam.Terrorist, GetTeamCaptain(CsTeam.Terrorist) },
            { CsTeam.CounterTerrorist, GetTeamCaptain(CsTeam.CounterTerrorist) },
        };
    }

    public void RemoveCaptain(CCSPlayerController player)
    {
        CsTeam team = player.Team;

        MatchManager? match = _matchService.GetCurrentMatch();

        if (
            match == null
            || !match.IsWarmup()
            || team == CsTeam.None
            || team == CsTeam.Spectator
            || _captains[team] == null
            || _captains[team] != player.SteamID.ToString()
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
        if (team == CsTeam.None || team == CsTeam.Spectator)
        {
            return null;
        }

        List<CCSPlayerController> players = MatchUtility.Players();

        return players.Find(player => player.SteamID.ToString() == _captains[team]);
    }

    public void ShowCaptains()
    {
        var captains = GetCaptains();

        foreach (CsTeam team in captains.Keys)
        {
            CCSPlayerController? captain = captains[team];
            if (captain == null)
            {
                _gameServer.Message(
                    HudDestination.Notify,
                    _localizer[
                        "captain.claim_hint",
                        TeamUtility.TeamNumToString((int)team),
                        ChatColors.Green,
                        CommandUtility.SilentChatTrigger
                    ]
                );
                continue;
            }

            _gameServer.Message(
                HudDestination.Notify,
                _localizer[
                    "captain.show",
                    TeamUtility.TeamNumToString((int)team),
                    team == CsTeam.Terrorist ? ChatColors.Gold : ChatColors.Blue,
                    captain.PlayerName
                ]
            );
        }
    }

    public void ClaimCaptain(CCSPlayerController player, CsTeam team)
    {
        if (_captains[team] == null)
        {
            _captains[team] = player.SteamID.ToString();

            _gameServer.Message(
                HudDestination.Alert,
                _localizer[
                    "captain.assigned",
                    player.PlayerName,
                    TeamUtility.TeamNumToString((int)team)
                ]
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
        if (player == null || player.IsBot || !player.IsValid)
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
            MatchMember? member = MatchUtility.GetMemberFromLineup(
                matchData,
                player.SteamID.ToString(),
                player.PlayerName
            );

            if (member?.captain == true && _captains[team] == null)
            {
                ClaimCaptain(player, team);
                return true;
            }
        }

        return _captains[team] == player?.SteamID.ToString();
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
