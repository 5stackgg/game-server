using FiveStack.Entities;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Translation;
using static SwiftlyS2.Shared.Helper;

namespace FiveStack;

public class CaptainSystem
{
    private readonly MatchEvents _matchEvents;
    private readonly GameServer _gameServer;
    private readonly MatchService _matchService;
    private readonly ILogger<CaptainSystem> _logger;
    private readonly ILocalizer _localizer;

    public Dictionary<Team, IPlayer?> _captains = new Dictionary<
        Team,
        IPlayer?
    >
    {
        { Team.T, null },
        { Team.CT, null },
    };

    public CaptainSystem(
        ILogger<CaptainSystem> logger,
        MatchEvents matchEvents,
        GameServer gameServer,
        MatchService matchService,
        ILocalizer localizer
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
        _captains[Team.T] = null;
        _captains[Team.CT] = null;
    }

    public void AutoSelectCaptains()
    {
        foreach (Team team in new[] { Team.T, Team.CT })
        {
            IPlayer? captain = GetTeamCaptain(team);
            if (
                captain == null
                || captain.IsFakeClient
                || !captain.IsValid
                || captain.Controller.Team != team
            )
            {
                AutoSelectCaptain(team);
            }
        }
    }

    public Dictionary<Team, IPlayer?> GetCaptains()
    {
        return new Dictionary<Team, IPlayer?>
        {
            { Team.T, GetTeamCaptain(Team.T) },
            { Team.CT, GetTeamCaptain(Team.CT) },
        };
    }

    public void RemoveCaptain(IPlayer player)
    {
        Team team = player.Controller.Team;

        MatchManager? match = _matchService.GetCurrentMatch();

        if (
            match == null
            || !(match.IsWarmup() || match.IsKnife())
            || team == Team.None
            || team == Team.Spectator
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
                { "player_name", player.Name },
            }
        );

        ShowCaptains();
    }

    public IPlayer? GetTeamCaptain(Team team)
    {
        if (team == Team.None || team == Team.Spectator)
        {
            return null;
        }

        IPlayer? captain = _captains[team];
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

        foreach (Team team in captains.Keys)
        {
            IPlayer? captain = captains[team];
            if (captain == null)
            {
                _gameServer.Message(
                    MessageType.Notify,
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
                MessageType.Notify,
                _localizer[
                    "captain.show",
                    TeamUtility.TeamNumToString((int)team),
                    team == Team.T ? ChatColors.Gold : ChatColors.Blue,
                    captain.Name
                ]
            );
        }
    }

    public void ClaimCaptain(IPlayer player, Team team, bool force = false)
    {
        if (player == null || player.IsFakeClient || !player.IsValid)
        {
            _logger.LogWarning(
                $"Unable to claim captain for playe because they are a bot or invalid"
            );
            return;
        }

        Team captainTeam = team;
        Team? expectedCaptainTeam = ResolveExpectedCaptainTeam(player);
        if (expectedCaptainTeam.HasValue && expectedCaptainTeam.Value != team)
        {
            if (!force)
            {
                _logger.LogWarning(
                    $"Unable to claim captain for player {player.Name} because expected side is {expectedCaptainTeam.Value}, not {team}"
                );
                return;
            }
            captainTeam = expectedCaptainTeam.Value;
        }

        if (!force && player.Controller.Team != captainTeam)
        {
            _logger.LogWarning(
                $"Unable to claim captain for player {player.Name} because they are not on the {captainTeam} team"
            );
            return;
        }

        if (_captains[captainTeam] == null || force)
        {
            SetCaptain(player, captainTeam, announce: true);
            _logger.LogInformation(
                $"Captain {player.Name} claimed captain spot for {captainTeam}"
            );

            _gameServer.Message(
                MessageType.Alert,
                _localizer[
                    "captain.assigned",
                    player.Name,
                    TeamUtility.TeamNumToString((int)captainTeam)
                ]
            );
        }

        ShowCaptains();
    }

    public bool IsCaptain(IPlayer player, Team team)
    {
        if (player == null || player.IsFakeClient || !player.IsValid)
        {
            return false;
        }

        if (team != Team.T && team != Team.CT)
        {
            return false;
        }

        Team? expectedCaptainTeam = ResolveExpectedCaptainTeam(player);
        if (expectedCaptainTeam.HasValue)
        {
            SetCaptain(player, expectedCaptainTeam.Value, announce: false);
            return expectedCaptainTeam.Value == team;
        }

        IPlayer? teamCaptain = GetTeamCaptain(team);
        if (
            player.Controller.Team == team
            && teamCaptain?.SteamID.ToString() == player.SteamID.ToString()
            && IsCaptainEntryValidForTeam(teamCaptain, team)
        )
        {
            return true;
        }

        return false;
    }

    private void AutoSelectCaptain(Team team)
    {
        var players = MatchUtility
            .Players()
            .FindAll(player =>
            {
                return player.Controller.Team == team
                    && player.IsFakeClient == false
                    && player.IsValid;
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

        IPlayer player = players[Random.Shared.Next(players.Count)];

        ClaimCaptain(player, team, true);
    }

    private Team? ResolveExpectedCaptainTeam(IPlayer player)
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
            player.Name
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

        Team expectedTeam = TeamUtility.GetLineupSide(
            matchData,
            currentMap,
            lineupId.Value,
            _gameServer.GetTotalRoundsPlayed()
        );

        if (expectedTeam != Team.T && expectedTeam != Team.CT)
        {
            return null;
        }

        return expectedTeam;
    }

    private bool IsCaptainEntryValidForTeam(IPlayer? player, Team team)
    {
        if (player == null || !player.IsValid || player.IsFakeClient)
        {
            return false;
        }

        Team? expectedCaptainTeam = ResolveExpectedCaptainTeam(player);
        if (expectedCaptainTeam.HasValue)
        {
            return expectedCaptainTeam.Value == team;
        }

        return player.Controller.Team == team;
    }

    private void SetCaptain(IPlayer player, Team team, bool announce)
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
                { "player_name", player.Name },
            }
        );
    }

    private void ClearCaptainBySteamId(string steamId, Team? ignoreTeam = null)
    {
        foreach (Team side in new[] { Team.T, Team.CT })
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
