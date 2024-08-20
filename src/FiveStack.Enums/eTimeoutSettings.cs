using System.ComponentModel;

namespace FiveStack.Enums;

public enum eTimeoutSettings
{
    [Description("Coach")]
    Coach,

    [Description("CoachAndPlayers")]
    CoachAndPlayers,

    [Description("Admin")]
    Admin,
}
