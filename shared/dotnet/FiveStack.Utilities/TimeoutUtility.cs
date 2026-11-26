using FiveStack.Enums;

namespace FiveStack.Utilities
{
    public static class TimeoutUtility
    {
        public static eTimeoutSettings TimeoutSettingStringToEnum(string state)
        {
            switch (state)
            {
                case "Coach":
                    return eTimeoutSettings.Coach;
                case "CoachAndCaptains":
                    return eTimeoutSettings.CoachAndCaptains;
                case "CoachAndPlayers":
                    return eTimeoutSettings.CoachAndPlayers;
                case "Admin":
                    return eTimeoutSettings.Admin;
                default:
                    throw new ArgumentException($"Unsupported timeout setting: {state}");
            }
        }

        // Never fed the clan tag: lineup players wear their team's short name
        // there, and team owners pick it freely.
        public static bool IsStaff(ePlayerRoles? connectedRole, ePlayerRoles? lineupRole)
        {
            return IsStaffRole(connectedRole) || IsStaffRole(lineupRole);
        }

        public static bool CanPause(
            eTimeoutSettings setting,
            ePlayerRoles? connectedRole,
            ePlayerRoles? lineupRole,
            bool isCoach,
            bool isCaptain
        )
        {
            if (IsStaffRole(connectedRole))
            {
                return true;
            }

            switch (setting)
            {
                case eTimeoutSettings.Coach:
                    return isCoach;
                case eTimeoutSettings.CoachAndCaptains:
                    return isCoach || isCaptain;
                case eTimeoutSettings.Admin:
                    return IsStaffRole(lineupRole);
            }

            return true;
        }

        // A refusal says who may call a tactical timeout under the setting that
        // refused it. CoachAndPlayers lets every player through.
        public static string TacticalTimeoutRefusal(eTimeoutSettings setting)
        {
            switch (setting)
            {
                case eTimeoutSettings.Coach:
                    return "timeout.not_allowed.tactical_timeout.coach";
                case eTimeoutSettings.CoachAndCaptains:
                    return "timeout.not_allowed.tactical_timeout.coach_and_captains";
                default:
                    return "timeout.not_allowed.tactical_timeout.admin";
            }
        }

        public static string ResumeRefusal(eTimeoutSettings setting)
        {
            switch (setting)
            {
                case eTimeoutSettings.Coach:
                    return "timeout.not_allowed.resume.coach";
                case eTimeoutSettings.CoachAndCaptains:
                    return "timeout.not_allowed.resume.coach_and_captains";
                default:
                    return "timeout.not_allowed.resume.admin";
            }
        }

        // Halftime and a round restore hold the round for a resume without the
        // map's status ever reading Paused.
        public static bool HasPauseToResume(eMapStatus status, bool waitingForResume)
        {
            return status == eMapStatus.Paused
                || (MatchPhaseUtility.PhaseOf(status) == eMatchPhase.InPlay && waitingForResume);
        }

        private static bool IsStaffRole(ePlayerRoles? role)
        {
            return role == ePlayerRoles.Administrator
                || role == ePlayerRoles.MatchOrganizer
                || role == ePlayerRoles.TournamentOrganizer;
        }
    }
}
