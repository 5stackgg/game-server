using System.ComponentModel;

namespace FiveStack.enums;

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

    [Description("TechTimeout")]
    TechTimeout,

    [Description("Unknown")]
    Unknown
}
