using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Utilities;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [ConsoleCommand("css_web_chat", "web message")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnWebMessage(CCSPlayerController? player, CommandInfo? command)
    {
        if (command == null || command.ArgCount < 2)
        {
            return;
        }

        (string text, bool organizer) = ChatUtility.ParseWebChat(
            Enumerable.Range(1, command.ArgCount - 1).Select(command.ArgByIndex).ToList()
        );

        if (organizer)
        {
            text = $" {ChatColors.Red}{ChatUtility.OrganizerTag}{ChatColors.White} {text}";
        }

        _gameServer.Message(HudDestination.Chat, text);
    }
}
