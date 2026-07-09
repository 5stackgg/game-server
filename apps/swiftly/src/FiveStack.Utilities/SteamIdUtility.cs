namespace FiveStack.Utilities
{
    public static class SteamIdUtility
    {
        public static string? ConvertSteamID64ToSteamID(ulong steamID64)
        {
            uint accountID = (uint)(steamID64 & 0xFFFFFFFF);
            uint instance = (uint)((steamID64 >> 32) & 0xFFFFF);
            uint accountType = (uint)((steamID64 >> 52) & 0xF);
            uint universe = (uint)((steamID64 >> 56) & 0xFF);

            char accountTypeChar = accountType switch
            {
                0 => 'I',
                1 => 'U',
                2 => 'M',
                3 => 'G',
                4 => 'A',
                5 => 'P',
                6 => 'C',
                7 => 'g',
                8 => 'T',
                _ => 'I',
            };

            if (accountID == 0)
            {
                return null;
            }

            return $"[{accountTypeChar}:{universe}:{accountID}:{instance}]";
        }
    }
}
