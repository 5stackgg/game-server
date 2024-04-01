using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.enums;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [ConsoleCommand("css_captain", "Shows / Claims Captain Spots")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnCaptain(CCSPlayerController? player, CommandInfo? command)
    {
        if (
            player == null
            || _currentMap == null
            || (
                MapStatusStringToEnum(_currentMap.status) != eMapStatus.Warmup
                && MapStatusStringToEnum(_currentMap.status) != eMapStatus.Knife
            )
        )
        {
            return;
        }

        CsTeam team = TeamNumToCSTeam(player.TeamNum);

        if (team == CsTeam.None || team == CsTeam.Spectator)
        {
            return;
        }

        // autoclaim captain
        if (_captains[team] == null)
        {
            ClaimCaptain(team, player);
        }

        ShowCaptains();
    }

    [ConsoleCommand("css_release-captain", "Release Captain Spot")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnReleaseCaptain(CCSPlayerController? player, CommandInfo? command)
    {
        if (
            player == null
            || _matchData == null
            || _currentMap == null
            || (
                MapStatusStringToEnum(_currentMap.status) != eMapStatus.Warmup
                && MapStatusStringToEnum(_currentMap.status) != eMapStatus.Knife
            )
        )
        {
            return;
        }

        CsTeam team = TeamNumToCSTeam(player.TeamNum);

        if (team == CsTeam.None || team == CsTeam.Spectator)
        {
            return;
        }

        _captains[team] = null;

        ShowCaptains();

        PublishGameEvent(
            "captain",
            new Dictionary<string, object>
            {
                { "claim", false },
                { "steam_id", player.SteamID.ToString() },
                { "player_name", player.PlayerName }
            }
        );
    }

    public void ShowCaptains()
    {
        foreach (var pair in _captains)
        {
            CsTeam? team = pair.Key;

            if (pair.Value == null)
            {
                Message(
                    HudDestination.Notify,
                    $"[{TeamNumToString((int)team)}] {ChatColors.Green}.captain to claim"
                );
                return;
            }

            Message(
                HudDestination.Notify,
                $"[{TeamNumToString((int)team)} Captain] {(team == CsTeam.Terrorist ? ChatColors.Gold : ChatColors.Blue)}{pair.Value.PlayerName}"
            );
        }
    }

    public void ClaimCaptain(CsTeam team, CCSPlayerController player, string? message = null)
    {
        if (player == null || _matchData == null)
        {
            return;
        }

        _captains[team] = player;
        if (message == null)
        {
            Message(
                HudDestination.Alert,
                $"{player.PlayerName} was assigned captain for the {TeamNumToString((int)team)}"
            );
        }

        PublishGameEvent(
            "captain",
            new Dictionary<string, object>
            {
                { "claim", true },
                { "steam_id", player.SteamID.ToString() },
                { "player_name", player.PlayerName }
            }
        );
    }
}
