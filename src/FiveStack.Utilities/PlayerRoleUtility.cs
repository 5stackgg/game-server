using FiveStack.Enums;

namespace FiveStack.Utilities
{
    public static class PlayerRoleUtility
    {
        public static ePlayerRoles PlayerRoleStringToEnum(string state)
        {
            switch (state)
            {
                case "administrator":
                    return ePlayerRoles.Administrator;
                case "match_organizer":
                case "tournament_organizer":
                    return ePlayerRoles.MatchOrganizer;
                case "stream":
                    return ePlayerRoles.Streamer;
                case "verified":
                    return ePlayerRoles.VerifiedUser;
                default:
                    return ePlayerRoles.User;
            }
        }
    }
}
