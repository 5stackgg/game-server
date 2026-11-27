namespace FiveStack.Utilities;

// The match type's mp_maxmoney caps regulation only; overtime always allows 16000.
public static class OvertimeMoney
{
    public const int MaxMoney = 16000;

    public static readonly string Command = $"mp_maxmoney {MaxMoney}";

    public static bool InOvertime(int roundsPlayed, int mr, bool overtime)
    {
        return overtime && mr > 0 && roundsPlayed >= mr * 2;
    }
}
