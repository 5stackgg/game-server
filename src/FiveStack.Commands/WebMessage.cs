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
        if (command == null)
        {
            return;
        }

        string message = command.ArgByIndex(1);

        if (message == null)
        {
            return;
        }

        _gameServer.Message(HudDestination.Chat, message);
    }
}
