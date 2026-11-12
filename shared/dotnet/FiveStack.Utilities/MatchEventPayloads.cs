using FiveStack.Enums;

namespace FiveStack.Utilities
{
    public static class MatchEventPayloads
    {
        // An api that predates the marker never reads it, and one that does sets
        // the replay's rounds aside on the Warmup and leaves the marked reset alone,
        // so a reset that lands after the replay's first round can't undo it.
        public const string ReplayKey = "replay";

        public static Dictionary<string, object> RestoreRound(
            int round,
            Guid mapId,
            bool replay = false
        )
        {
            var data = new Dictionary<string, object>
            {
                { "round", round },
                { "match_map_id", mapId },
            };

            if (replay)
            {
                data[ReplayKey] = true;
            }

            return data;
        }

        public static Dictionary<string, object> MapStatus(
            eMapStatus status,
            Guid? winningLineupId,
            DateTime time,
            Guid? mapId,
            bool replay = false
        )
        {
            var data = new Dictionary<string, object>
            {
                { "status", status.ToString() },
                { "winning_lineup_id", winningLineupId?.ToString() ?? "" },
                { "time", time },
            };

            if (mapId != null)
            {
                data["match_map_id"] = mapId.Value.ToString();
            }

            if (replay && status == eMapStatus.Warmup)
            {
                data[ReplayKey] = true;
            }

            return data;
        }
    }
}
