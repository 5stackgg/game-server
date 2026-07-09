using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [ConsoleCommand("css_web_chat", "web message")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnWebMessage(CCSPlayerController? player, CommandInfo? command)
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

        if (message.StartsWith("[organizer]"))
        {
            message =
                $" {ChatColors.Red}[organizer]{ChatColors.White} {message.Replace("[organizer]", "")}";
        }

        _gameServer.Message(HudDestination.Chat, message);
    }
}
