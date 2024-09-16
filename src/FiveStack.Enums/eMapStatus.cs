using System.ComponentModel;

namespace FiveStack.Enums;

public enum eMapStatus
{
    [Description("Scheduled")]
    Scheduled,

    [Description("Warmup")]
    Warmup,

    [Description("Knife")]
    Knife,

    [Description("Live")]
    Live,

    [Description("Overtime")]
    Overtime,

    [Description("Finished")]
    Finished,

    [Description("Paused")]
    Paused,

    [Description("Unknown")]
    Unknown,

    [Description("Surrender")]
    Surrender,
}
