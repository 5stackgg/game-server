using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace PlayCs;

public partial class PlayCsPlugin
{
    [ConsoleCommand("css_stay", "")]
    public void OnStay(CCSPlayerController? player, CommandInfo? command)
    {
        Message(
            HudDestination.Alert,
            $"captain picked to {ChatColors.Red}stay {ChatColors.Default}sides"
        );

        UpdatePhase(ePhase.Live);
    }

    [ConsoleCommand("css_switch", "")]
    public void onSwitch(CCSPlayerController? player, CommandInfo? command)
    {
        Message(
            HudDestination.Alert,
            $"captain picked to {ChatColors.Red}swap {ChatColors.Default}sides"
        );

        SendCommands(new[] { "mp_swapteams" });

        UpdatePhase(ePhase.Live);
    }
}
