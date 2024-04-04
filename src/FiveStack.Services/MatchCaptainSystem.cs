using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public class MatchCaptainSystem
{
    private FiveStackMatch? _match;
    private readonly GameEvents _gameEvents;
    private readonly GameServer _gameServer;
    private readonly ILogger<MatchCaptainSystem> _logger;

    public Dictionary<CsTeam, CCSPlayerController?> _captains = new Dictionary<
        CsTeam,
        CCSPlayerController?
    >
    {
        { CsTeam.Terrorist, null },
        { CsTeam.CounterTerrorist, null }
    };

    public MatchCaptainSystem(
        ILogger<MatchCaptainSystem> logger,
        GameEvents gameEvents,
        GameServer gameServer
    )
    {
        _logger = logger;
        _gameEvents = gameEvents;
        _gameServer = gameServer;
    }

    public void Setup(FiveStackMatch match)
    {
        _match = match;
    }

    public void AutoSelectCaptains(FiveStackMatch match)
    {
        if (_captains[CsTeam.Terrorist] == null)
        {
            AutoSelectCaptain(match, CsTeam.Terrorist);
        }

        if (_captains[CsTeam.CounterTerrorist] == null)
        {
            AutoSelectCaptain(match, CsTeam.CounterTerrorist);
        }
    }

    public void RemoveTeamCaptain(FiveStackMatch match, CCSPlayerController player, CsTeam team)
    {
        _captains[team] = null;

        _gameEvents.PublishGameEvent(
            match.id,
            "captain",
            new Dictionary<string, object>
            {
                { "claim", false },
                { "steam_id", player.SteamID.ToString() },
                { "player_name", player.PlayerName }
            }
        );
    }

    public bool TeamHasCaptain(CsTeam team)
    {
        return _captains[team] == null;
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
                return;
            }

            _gameServer.Message(
                HudDestination.Notify,
                $"[{TeamUtility.TeamNumToString((int)team)} Captain] {(team == CsTeam.Terrorist ? ChatColors.Gold : ChatColors.Blue)}{pair.Value.PlayerName}"
            );
        }
    }

    public void ClaimCaptain(
        FiveStackMatch match,
        CsTeam team,
        CCSPlayerController player,
        string? message = null
    )
    {
        if (player == null)
        {
            return;
        }

        _captains[team] = player;
        if (message == null)
        {
            _gameServer.Message(
                HudDestination.Alert,
                $"{player.PlayerName} was assigned captain for the {TeamUtility.TeamNumToString((int)team)}"
            );
        }

        _gameEvents.PublishGameEvent(
            match.id,
            "captain",
            new Dictionary<string, object>
            {
                { "claim", true },
                { "steam_id", player.SteamID.ToString() },
                { "player_name", player.PlayerName }
            }
        );
    }

    public bool IsCaptain(CCSPlayerController player, CsTeam? team)
    {
        if (team != null)
        {
            return _captains[team ?? CsTeam.None]?.SteamID == player.SteamID;
        }

        return _captains[CsTeam.CounterTerrorist]?.SteamID == player.SteamID
            || _captains[CsTeam.Terrorist]?.SteamID == player.SteamID;
    }

    private void ResetCaptains()
    {
        _captains[CsTeam.Terrorist] = null;
        _captains[CsTeam.CounterTerrorist] = null;
    }

    private void AutoSelectCaptain(FiveStackMatch match, CsTeam team)
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

        ClaimCaptain(
            match,
            team,
            player,
            $" {(team == CsTeam.Terrorist ? ChatColors.Gold : ChatColors.Blue)}{TeamUtility.TeamNumToString((int)team)}'s {ChatColors.Default}captain was auto selected to be {ChatColors.Red}{player.PlayerName}"
        );
    }
}
