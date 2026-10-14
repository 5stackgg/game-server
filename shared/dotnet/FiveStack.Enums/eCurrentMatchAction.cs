namespace FiveStack.Enums;

// What a server does with the api's answer to current-match/:serverId.
public enum eCurrentMatchAction
{
    // An empty body: no match is assigned, so the one it holds is cleared.
    Clear,

    // A match to set up, or to set up again if it is the one it holds.
    Setup,

    // A body that is not a match: the server keeps what it has.
    Ignore,
}
