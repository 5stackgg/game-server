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
}
