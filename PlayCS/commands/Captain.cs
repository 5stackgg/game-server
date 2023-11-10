using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace PlayCs;

/**
 * TODO : show whos not ready
 * TODO : player switches side remove them as captain
 * TODO : claim event
 */
public partial class PlayCsPlugin
{
    [ConsoleCommand("css_c", "Shows / Claims Captain Spots")]
    [ConsoleCommand("css_captain", "Shows / Claims Captain Spots")]
    public void OnCaptain(CCSPlayerController? player, CommandInfo? command)
    {
        if (
            PhaseStringToEnum(matchData.status) != ePhase.Warmup
            && PhaseStringToEnum(matchData.status) != ePhase.Knife
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
        if (Captains[team] == null)
        {
            ClaimCaptain(team, player);
        }

        ShowCaptains();
    }

    private void ShowCaptains()
    {
        foreach (var pair in Captains)
        {
            CsTeam team = pair.Key;
            CCSPlayerController captain = pair.Value;

            if (captain == null)
            {
                Message(
                    HudDestination.Notify,
                    $"[{TeamNumToString((int)team)}] {ChatColors.Green}!captain to claim"
                );
                return;
            }
            Message(
                HudDestination.Notify,
                $"[{TeamNumToString((int)team)}] {(team == CsTeam.Terrorist ? ChatColors.Gold : ChatColors.Blue)}{captain.PlayerName}"
            );
        }
    }

    [ConsoleCommand("css_rc", "Release Captain Spot")]
    [ConsoleCommand("css_release_captain", "Release Captain Spot")]
    public void OnReleaseCaptain(CCSPlayerController? player, CommandInfo? command)
    {
        if (
            PhaseStringToEnum(matchData.status) != ePhase.Warmup
            && PhaseStringToEnum(matchData.status) != ePhase.Knife
        )
        {
            return;
        }

        CsTeam team = TeamNumToCSTeam(player.TeamNum);

        if (team == CsTeam.None || team == CsTeam.Spectator)
        {
            return;
        }

        Captains[team] = null;

        ShowCaptains();
    }

    private void ClaimCaptain(CsTeam team, CCSPlayerController player, string? message = null)
    {
        Captains[team] = player;
        if (message == null)
        {
            Message(
                HudDestination.Alert,
                $"{player.PlayerName} was assigned captain for the {TeamNumToString((int)team)}"
            );
        }

        Eventing.PublishMatchEvent(
            matchData!.id,
            new Eventing.EventData<Dictionary<string, object>>
            {
                @event = "captain",
                data = new Dictionary<string, object>
                {
                    { "steam_id", player.SteamID },
                    { "player_name", player.PlayerName },
                    { "team", TeamNumToString((int)team) },
                }
            }
        );
    }
}
