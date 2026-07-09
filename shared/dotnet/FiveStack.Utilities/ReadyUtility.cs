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
    }
}
