using FiveStack.Entities;
using FiveStack.Enums;

namespace FiveStack.Utilities
{
    public static class ReadyUtility
    {
        public static eReadySettings ReadySettingStringToEnum(string state)
        {
            switch (state)
            {
                case "Captains":
                    return eReadySettings.Captains;
                case "Coach":
                    return eReadySettings.Coach;
                case "Admin":
                    return eReadySettings.Admin;
                case "Players":
                    return eReadySettings.Players;
                default:
                    throw new ArgumentException($"Unsupported ready setting: {state}");
            }
        }

        // The ready count is held to the lineups' size, so a coach, caster or
        // spectator readying would start the match a player short.
        public static bool CanReady(
            eReadySettings setting,
            MatchData matchData,
            string steamId,
            string playerName,
            bool isCoach,
            bool isCaptain
        )
        {
            bool onLineup = LineupMembership.LineupNumber(matchData, steamId, playerName) != null;

            switch (setting)
            {
                case eReadySettings.Coach:
                    return isCoach;
                case eReadySettings.Captains:
                    return isCaptain && onLineup;
                case eReadySettings.Admin:
                    return false;
            }

            return onLineup;
        }
    }
}
