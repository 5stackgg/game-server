namespace FiveStack.Utilities;

// A refused client used to be handed an empty Steam ticket, which made the
// engine challenge it again forever, each try holding a slot until the server
// was full. With a server password the client is handed this one instead, and
// the engine refuses it once. It holds a control character, which no password
// can. The engine reads an empty sv_password or "none" as no password at all,
// and then this would let the client in, so those keep the empty ticket.
public static class ConnectRefusal
{
    public const string Password = "\u0001refused";

    public static bool ByPassword(string? serverPassword)
    {
        return !string.IsNullOrEmpty(serverPassword)
            && !serverPassword.Equals("none", StringComparison.OrdinalIgnoreCase)
            && serverPassword != Password;
    }
}
