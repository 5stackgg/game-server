using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using PlayCS.enums;

namespace PlayCs;

public partial class PlayCsPlugin
{
    [ConsoleCommand("css_coach", "Shows / Claims Coach Spots ")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnCoach(CCSPlayerController? player, CommandInfo? command)
    {
        if (
            player == null
            || _matchData == null
            || (
                GameStateStringToEnum(_matchData.status) != eGameState.Warmup
            )
        )
        {
            return;
        }

        _coaches[CsTeam.Terrorist] = player;

        ShowCoaches();
    }

    [ConsoleCommand("css_release_coach", "Release Coach Spot")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnReleaseCoach(CCSPlayerController? player, CommandInfo? command)
    {
        if (
            player == null
            || _matchData == null
            || (
                GameStateStringToEnum(_matchData.status) != eGameState.Warmup
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

        _coaches[team] = null;

        ShowCoaches();
    }

    public void ShowCoaches()
    {
        foreach (var pair in _coaches)
        {
            CsTeam? team = pair.Key;

            if (pair.Value == null)
            {
                Message(
                    HudDestination.Notify,
                    $"[{TeamNumToString((int)team)}] {ChatColors.Green}!coach .t or !coach .ct to coach"
                );
                return;
            }

            Message(
                HudDestination.Notify,
                $"[{TeamNumToString((int)team)}] {(team == CsTeam.Terrorist ? ChatColors.Gold : ChatColors.Blue)}{pair.Value.PlayerName}"
            );
        }
    }
}
