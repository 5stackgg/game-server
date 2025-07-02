using System.ComponentModel;

namespace FiveStack.Enums;

public enum eMapStatus
{
    [Description("Knife")]
    Knife,

    [Description("Live")]
    Live,

    [Description("Warmup")]
    Warmup,

    [Description("Paused")]
    Paused,

    [Description("Scheduled")]
    Scheduled,

    [Description("Overtime")]
    Overtime,

    [Description("UploadingDemo")]
    UploadingDemo,

    [Description("Finished")]
    Finished,

    [Description("Surrendered")]
    Surrendered,

    [Description("Unknown")]
    Unknown,
}
