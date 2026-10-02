using FiveStack.Entities;

namespace FiveStack.Utilities
{
    // The engine can set Valve's report mute again a second or so after it is
    // cleared, so a single clear on connect or spawn does not stick.
    public sealed class CommunicationAbuseMute
    {
        public const float RecheckInterval = 0.5f;
        public const int RecheckCount = 20;
        public const int MaxRestarts = 5;

        private readonly Dictionary<ulong, int> _checksLeft = new();
        private readonly Dictionary<ulong, int> _restarts = new();
        private readonly HashSet<ulong> _announced = new();

        // The match data decides, not the voice flags: they are only applied
        // once the player is placed, and a reconnect resets them.
        public static bool ShouldClear(
            bool hasCommunicationAbuseMute,
            bool voiceMuted,
            MatchData? matchData,
            string steamId,
            string playerName
        )
        {
            if (!hasCommunicationAbuseMute || voiceMuted || matchData == null)
            {
                return false;
            }

            return !matchData
                .lineup_1.lineup_players.Concat(matchData.lineup_2.lineup_players)
                .Any(member =>
                    member.is_muted
                    && (
                        member.steam_id == steamId
                        || (
                            member.steam_id == null
                            && member.placeholder_name.StartsWith(playerName)
                        )
                    )
                );
        }

        public void Connected(ulong steamId)
        {
            _restarts.Remove(steamId);
            _announced.Remove(steamId);
            Watch(steamId);
        }

        public void Watch(ulong steamId)
        {
            _checksLeft[steamId] = RecheckCount;
        }

        public void Reasserted(ulong steamId)
        {
            int restarts = _restarts.GetValueOrDefault(steamId);

            if (restarts >= MaxRestarts)
            {
                return;
            }

            _restarts[steamId] = restarts + 1;
            Watch(steamId);
        }

        public bool FirstClear(ulong steamId)
        {
            return _announced.Add(steamId);
        }

        public List<ulong> Due()
        {
            List<ulong> due = _checksLeft.Keys.ToList();

            foreach (ulong steamId in due)
            {
                _checksLeft[steamId]--;

                if (_checksLeft[steamId] == 0)
                {
                    _checksLeft.Remove(steamId);
                }
            }

            return due;
        }
    }
}
