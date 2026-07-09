namespace FiveStack.Utilities
{
    public static class CommandUtility
    {
        public static string PublicChatTrigger = ".";
        public static string SilentChatTrigger = "/";

        public static void Initialize(string publicChatTrigger, string silentChatTrigger)
        {
            PublicChatTrigger = publicChatTrigger;
            SilentChatTrigger = silentChatTrigger;
        }
    }
}
