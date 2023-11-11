using System.ComponentModel;

namespace PlayCS.enums;

public enum ePhase
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

    [Description("Unknown")]
    Unknown
}
