using System.ComponentModel;

namespace FiveStack.Enums;

public enum eTimeoutSettings
{
    [Description("Coach")]
    Coach,

    [Description("CoachAndCaptains")]
    CoachAndCaptains,

    [Description("CoachAndPlayers")]
    CoachAndPlayers,

    [Description("Admin")]
    Admin,
}
