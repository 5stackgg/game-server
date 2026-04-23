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

    public void Reset()
    {
        _captains.Clear();
        _captains[CsTeam.Terrorist] = null;
        _captains[CsTeam.CounterTerrorist] = null;
    }

    public void AutoSelectCaptains()
    {
        foreach (CsTeam team in new[] { CsTeam.Terrorist, CsTeam.CounterTerrorist })
        {
            CCSPlayerController? captain = GetTeamCaptain(team);
            if (captain == null || captain.IsBot || !captain.IsValid || captain.Team != team)
            {
                AutoSelectCaptain(team);
            }
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
            || !(match.IsWarmup() || match.IsKnife())
            || team == CsTeam.None
            || team == CsTeam.Spectator
            || _captains[team] == null
            || _captains[team]?.SteamID.ToString() != player.SteamID.ToString()
        )
        {
            return;
        }

        _captains[team] = null;
        ClearCaptainBySteamId(player.SteamID.ToString());

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

        CCSPlayerController? captain = _captains[team];
        if (!IsCaptainEntryValidForTeam(captain, team))
        {
            _captains[team] = null;
            return null;
        }

        return captain;
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

    public void ClaimCaptain(CCSPlayerController player, CsTeam team, bool force = false)
    {
        if (player == null || player.IsBot || !player.IsValid)
        {
            _logger.LogWarning(
                $"Unable to claim captain for playe because they are a bot or invalid"
            );
            return;
        }

        CsTeam captainTeam = team;
        CsTeam? expectedCaptainTeam = ResolveExpectedCaptainTeam(player);
        if (expectedCaptainTeam.HasValue && expectedCaptainTeam.Value != team)
        {
            if (!force)
            {
                _logger.LogWarning(
                    $"Unable to claim captain for player {player.PlayerName} because expected side is {expectedCaptainTeam.Value}, not {team}"
                );
                return;
            }
            captainTeam = expectedCaptainTeam.Value;
        }

        if (!force && player.Team != captainTeam)
        {
            _logger.LogWarning(
                $"Unable to claim captain for player {player.PlayerName} because they are not on the {captainTeam} team"
            );
            return;
        }

        if (_captains[captainTeam] == null || force)
        {
            SetCaptain(player, captainTeam, announce: true);
            _logger.LogInformation(
                $"Captain {player.PlayerName} claimed captain spot for {captainTeam}"
            );

            _gameServer.Message(
                HudDestination.Alert,
                _localizer[
                    "captain.assigned",
                    player.PlayerName,
                    TeamUtility.TeamNumToString((int)captainTeam)
                ]
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

        CsTeam? expectedCaptainTeam = ResolveExpectedCaptainTeam(player);
        if (expectedCaptainTeam.HasValue)
        {
            SetCaptain(player, expectedCaptainTeam.Value, announce: false);
            return expectedCaptainTeam.Value == team;
        }

        CCSPlayerController? teamCaptain = GetTeamCaptain(team);
        if (
            player.Team == team
            && teamCaptain?.SteamID.ToString() == player.SteamID.ToString()
            && IsCaptainEntryValidForTeam(teamCaptain, team)
        )
        {
            return true;
        }

        return false;
    }

    private void AutoSelectCaptain(CsTeam team)
    {
        var players = MatchUtility
            .Players()
            .FindAll(player =>
            {
                return player.Team == team && player.IsBot == false && player.IsValid;
            });

        foreach (var _player in players)
        {
            if (IsCaptain(_player, team))
            {
                ClaimCaptain(_player, team, true);
                return;
            }
        }

        if (players.Count == 0)
        {
            return;
        }

        CCSPlayerController player = players[Random.Shared.Next(players.Count)];

        ClaimCaptain(player, team, true);
    }

    private CsTeam? ResolveExpectedCaptainTeam(CCSPlayerController player)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();
        MatchMap? currentMap = match?.GetCurrentMap();

        if (matchData == null || currentMap == null)
        {
            return null;
        }

        MatchMember? member = MatchUtility.GetMemberFromLineup(
            matchData,
            player.SteamID.ToString(),
            player.PlayerName
        );

        if (member?.captain != true)
        {
            return null;
        }

        Guid? lineupId = MatchUtility.GetPlayerLineup(matchData, player);
        if (!lineupId.HasValue)
        {
            return null;
        }

        CsTeam expectedTeam = TeamUtility.GetLineupSide(
            matchData,
            currentMap,
            lineupId.Value,
            _gameServer.GetTotalRoundsPlayed()
        );

        if (expectedTeam != CsTeam.Terrorist && expectedTeam != CsTeam.CounterTerrorist)
        {
            return null;
        }

        return expectedTeam;
    }

    private bool IsCaptainEntryValidForTeam(CCSPlayerController? player, CsTeam team)
    {
        if (player == null || !player.IsValid || player.IsBot)
        {
            return false;
        }

        CsTeam? expectedCaptainTeam = ResolveExpectedCaptainTeam(player);
        if (expectedCaptainTeam.HasValue)
        {
            return expectedCaptainTeam.Value == team;
        }

        return player.Team == team;
    }

    private void SetCaptain(CCSPlayerController player, CsTeam team, bool announce)
    {
        ClearCaptainBySteamId(player.SteamID.ToString(), team);
        _captains[team] = player;

        if (!announce)
        {
            return;
        }

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

    private void ClearCaptainBySteamId(string steamId, CsTeam? ignoreTeam = null)
    {
        foreach (CsTeam side in new[] { CsTeam.Terrorist, CsTeam.CounterTerrorist })
        {
            if (ignoreTeam.HasValue && ignoreTeam.Value == side)
            {
                continue;
            }

            if (_captains[side]?.SteamID.ToString() == steamId)
            {
                _captains[side] = null;
            }
        }
    }
}
