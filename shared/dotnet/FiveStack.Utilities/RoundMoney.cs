namespace FiveStack.Utilities
{
    public readonly record struct PlayerMoney(ulong SteamId, int Spent, int? CashEarned);

    // CashEarned is a running total for the whole map, so a round's income is
    // what it grew by since that round started. A player with no reading at the
    // start joined mid-round and is owed nothing for it.
    public static class RoundMoney
    {
        public static void Baseline(
            Dictionary<ulong, int> atRoundStart,
            IEnumerable<PlayerMoney> players
        )
        {
            atRoundStart.Clear();

            foreach (PlayerMoney player in players)
            {
                if (player.SteamId == 0 || player.CashEarned == null)
                {
                    continue;
                }

                atRoundStart[player.SteamId] = player.CashEarned.Value;
            }
        }

        public static int Earned(IReadOnlyDictionary<ulong, int> atRoundStart, PlayerMoney player)
        {
            if (
                player.CashEarned == null
                || !atRoundStart.TryGetValue(player.SteamId, out int start)
            )
            {
                return 0;
            }

            return Math.Max(0, player.CashEarned.Value - start);
        }

        public static List<Dictionary<string, object>> Payload(
            IReadOnlyDictionary<ulong, int> atRoundStart,
            IEnumerable<PlayerMoney> players
        )
        {
            var sent = new List<Dictionary<string, object>>();

            foreach (PlayerMoney player in players)
            {
                if (player.SteamId == 0)
                {
                    continue;
                }

                sent.Add(
                    new Dictionary<string, object>
                    {
                        { "steam_id", player.SteamId.ToString() },
                        { "spent", player.Spent },
                        { "earned", Earned(atRoundStart, player) },
                    }
                );
            }

            return sent;
        }
    }
}
