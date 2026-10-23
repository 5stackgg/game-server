using FiveStack.Enums;

namespace FiveStack.Utilities;

public static class PlayerDisconnectUtility
{
    // Everyone is reported, not only the lineups: the api was told about
    // admins, organizers, streamers and anyone let into an open match when
    // they connected, and an unknown steam id costs it nothing.
    public static eDisconnectHandling Handle(bool isBot, bool overCapacityKick, bool isLineupMember)
    {
        if (isBot)
        {
            return eDisconnectHandling.Ignore;
        }

        if (overCapacityKick || !isLineupMember)
        {
            return eDisconnectHandling.Report;
        }

        return eDisconnectHandling.LineupMemberLeft;
    }
}
