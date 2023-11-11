using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using PlayCS.enums;

namespace PlayCs;

public partial class PlayCsPlugin
{
    [ConsoleCommand("css_c", "Shows / Claims Captain Spots")]
    [ConsoleCommand("css_captain", "Shows / Claims Captain Spots")]
    public void OnCaptain(CCSPlayerController? player, CommandInfo? command)
    {
        if (
            player == null
            || _matchData == null
            || (
                PhaseStringToEnum(_matchData.status) != ePhase.Warmup
                && PhaseStringToEnum(_matchData.status) != ePhase.Knife
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

    [ConsoleCommand("css_rc", "Release Captain Spot")]
    [ConsoleCommand("css_release_captain", "Release Captain Spot")]
    public void OnReleaseCaptain(CCSPlayerController? player, CommandInfo? command)
    {
        if (
            player == null
            || _matchData == null
            || (
                PhaseStringToEnum(_matchData.status) != ePhase.Warmup
                && PhaseStringToEnum(_matchData.status) != ePhase.Knife
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
    }

    public void ShowCaptains()
    {
        foreach (var pair in _captains)
        {
            CsTeam team = pair.Key;

            if (pair.Value == null)
            {
                Message(
                    HudDestination.Notify,
                    $"[{TeamNumToString((int)team)}] {ChatColors.Green}!captain to claim"
                );
                return;
            }

            Message(
                HudDestination.Notify,
                $"[{TeamNumToString((int)team)}] {(team == CsTeam.Terrorist ? ChatColors.Gold : ChatColors.Blue)}{pair.Value.PlayerName}"
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

        _redis.PublishMatchEvent(
            _matchData.id,
            new Redis.EventData<Dictionary<string, object>>
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
