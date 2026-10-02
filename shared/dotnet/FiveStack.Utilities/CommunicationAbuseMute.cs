namespace FiveStack.Utilities
{
    // The engine can set Valve's report mute again a second or so after it is
    // cleared, so a single clear on connect or spawn does not stick.
    public sealed class CommunicationAbuseMute
    {
        public const float RecheckInterval = 0.5f;
        public const int RecheckCount = 20;

        private readonly Dictionary<ulong, int> _checksLeft = new();

        public static bool ShouldClear(bool hasCommunicationAbuseMute, bool mutedByUs)
        {
            return hasCommunicationAbuseMute && !mutedByUs;
        }

        public void Watch(ulong steamId)
        {
            _checksLeft[steamId] = RecheckCount;
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
