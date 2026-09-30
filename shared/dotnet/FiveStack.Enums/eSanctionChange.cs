namespace FiveStack.Enums;

[Flags]
public enum eSanctionChange
{
    None = 0,
    Banned = 1,
    Muted = 2,
    Unmuted = 4,
    Gagged = 8,
    Ungagged = 16,
}
