using System.ComponentModel;

namespace FiveStack.enums;

public enum eGameState
{
    [Description("Finished")]
    Finished,

    [Description("Knife")]
    Knife,

    [Description("Live")]
    Live,

    [Description("Overtime")]
    Overtime,

    [Description("Paused")]
    Paused,

    [Description("Scheduled")]
    Scheduled,

    [Description("TechTimeout")]
    TechTimeout,

    [Description("Warmup")]
    Warmup,

    [Description("PickingPlayers")]
    PickingPlayers,

    [Description("Unknown")]
    Unknown
}
