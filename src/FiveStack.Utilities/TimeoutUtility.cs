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
    }
}
