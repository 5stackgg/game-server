using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [Command("web_chat", registerRaw: true, permission: "")]
    public void OnWebMessage(ICommandContext context)
    {
        if (context.IsSentByPlayer)
        {
            return;
        }

        string? message = context.Args.Length > 0 ? context.Args[0] : null;

        if (message == null)
        {
            return;
        }

        if (message.StartsWith("[organizer]"))
        {
            message = $" [red][organizer][white] {message.Replace("[organizer]", "")}";
        }

        _gameServer.Message(MessageType.Chat, message);
    }
}
