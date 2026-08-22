namespace FiveStack.Enums;

// What a practice server's connect hook should do with a joining client. The
// decision is taken before any engine state is touched, so it can be tested.
public enum ePracticeConnect
{
    // Known client: swap the password parameter for the server's own so the
    // engine's check passes.
    Authorized,

    // Unknown, but not provably wrong: leave the connect alone and let the
    // engine check the password it was given.
    PasswordCheck,

    // Blank the auth ticket so the connect fails.
    Reject,
}
