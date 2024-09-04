using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [ConsoleCommand("css_web_chat", "web message")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnWebMessGe(CCSPlayerController? player, CommandInfo? command)
    {
        string message = command.ArgByIndex(1);

        _gameServer.Message(HudDestination.Chat, message);
    }
}
