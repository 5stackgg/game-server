using System.ComponentModel;

namespace FiveStack.Enums;

public enum ePlayerRoles
{
    [Description("Basic User")]
    User,

    [Description("Verified User")]
    VerifiedUser,

    [Description("Streamer")]
    Streamer,

    [Description("Ability Manage Matches and bypass restrictions")]
    MatchOrganizer,

    [Description("Ability Create and Manage Tournaments")]
    TournamentOrganizer,

    [Description("Administrator")]
    Administrator,
}
