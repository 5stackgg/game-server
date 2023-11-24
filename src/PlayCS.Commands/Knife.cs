using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using PlayCS.enums;

namespace PlayCs;

public partial class PlayCsPlugin
{
    [ConsoleCommand("css_stay", "")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnStay(CCSPlayerController? player, CommandInfo? command)
    {
        if (player == null || KnifeWinningTeam == null || !IsKnife())
        {
            return;
        }

        if (_captains[(CsTeam)KnifeWinningTeam]?.SteamID != player.SteamID)
        {
            Message(HudDestination.Chat, $" {ChatColors.Red}You are not the captain!", player);
            return;
        }

        Message(
            HudDestination.Alert,
            $"captain picked to {ChatColors.Red}stay {ChatColors.Default}sides"
        );

        UpdateGameState(eGameState.Live);
    }

    [ConsoleCommand("css_switch", "")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnSwitch(CCSPlayerController? player, CommandInfo? command)
    {
        if (player == null || _matchData == null || KnifeWinningTeam == null || !IsKnife())
        {
            return;
        }

        if (_captains[(CsTeam)KnifeWinningTeam]?.SteamID != player.SteamID)
        {
            Message(HudDestination.Chat, $" {ChatColors.Red}You are not the captain!", player);
            return;
        }

        Message(
            HudDestination.Alert,
            $"captain picked to {ChatColors.Red}swap {ChatColors.Default}sides"
        );

        _redis.PublishMatchEvent(
            _matchData.id,
            new Redis.EventData<Dictionary<string, object>>
            {
                @event = "switch",
                data = new Dictionary<string, object>()
            }
        );

        SendCommands(new[] { "mp_swapteams" });

        UpdateGameState(eGameState.Live);
    }

    [ConsoleCommand("css_skip_knife", "Skips knife round")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnSkipKnife(CCSPlayerController? player, CommandInfo? command)
    {
        if (!IsKnife())
        {
            return;
        }

        Message(HudDestination.Center, $"Skipping Knife.", player);

        UpdateGameState(eGameState.Live);
    }
}
