using System.ComponentModel;

namespace FiveStack.Enums;

public enum eWinReason
{
    [Description("Terrorists Win")]
    TerroristsWin,

    [Description("CTs Win")]
    CTsWin,

    [Description("Bomb Exploded")]
    BombExploded,

    [Description("Time Ran Out")]
    TimeRanOut,

    [Description("Bomb Defused")]
    BombDefused,

    [Description("Unknown")]
    Unknown,
}
