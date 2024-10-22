using CounterStrikeSharp.API.Core;

namespace FiveStack.Utilities
{
    public static class CommandUtility
    {
        public static string PublicChatTrigger =
            CoreConfig.PublicChatTrigger.FirstOrDefault() ?? ".";
        public static string SilentChatTrigger =
            CoreConfig.SilentChatTrigger.FirstOrDefault() ?? ".";
    }
}
