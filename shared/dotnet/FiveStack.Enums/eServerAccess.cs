namespace FiveStack.Enums;

// What a restricted server's ConnectClient hook does with a joining client.
public enum eServerAccess
{
    // No access list loaded yet: leave the connect to the engine.
    Unknown,

    // Not restricted: leave the connect to the engine, sv_password included.
    Open,

    // On the access list: swap in the server's own sv_password.
    Allowed,

    // Not on the access list: blank the auth ticket so the connect fails.
    Denied,
}
