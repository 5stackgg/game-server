using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.enums;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [ConsoleCommand("css_forfit", "Forfits the match")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnForfit(CCSPlayerController? player, CommandInfo? command)
    {
        if (player == null)
        {
            return;
        }

        Logger.LogWarning("FORFIT");
    }
}
