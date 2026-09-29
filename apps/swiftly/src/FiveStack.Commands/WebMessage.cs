using FiveStack.Utilities;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [Command("web_chat", registerRaw: false, permission: "")]
    public void OnWebMessage(ICommandContext context)
    {
        if (context.IsSentByPlayer || context.Args.Length == 0)
        {
            return;
        }

        (string text, bool organizer) = ChatUtility.ParseWebChat(context.Args);

        if (organizer)
        {
            text = $" [red]{ChatUtility.OrganizerTag}[white] {text}";
        }

        _gameServer.Message(MessageType.Chat, text);
    }
}
