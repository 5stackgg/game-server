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
                case "streamer":
                    return ePlayerRoles.Streamer;
                case "verified_user":
                    return ePlayerRoles.VerifiedUser;
                default:
                    return ePlayerRoles.User;
            }
        }
    }
}
