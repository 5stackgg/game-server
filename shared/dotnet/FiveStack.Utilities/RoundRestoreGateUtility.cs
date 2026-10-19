using FiveStack.Entities;

namespace FiveStack.Utilities
{
    public static class RoundRestoreGateUtility
    {
        // The api reads this off the RCON reply to tell a refused restore_round
        // from one that was sent on.
        public const string RefusedPrefix = "restore_round refused:";

        public static string? Refusal(
            bool restoresDisabled,
            bool alreadyRestoring,
            int connectedPlayers,
            int expectedPlayers,
            string matchType
        )
        {
            if (restoresDisabled)
            {
                return $"round restores are disabled in {matchType}";
            }

            if (alreadyRestoring)
            {
                return "a round restore is already in progress";
            }

            if (connectedPlayers < expectedPlayers)
            {
                return $"waiting for all players to reconnect ({connectedPlayers}/{expectedPlayers})";
            }

            return null;
        }

        // A map's backups are appended as rounds are played, so a round played
        // again after a restore is listed twice and the newer backup is the one
        // that matches what was replayed.
        public static BackupRound? LatestBackup(IEnumerable<BackupRound> rounds, int round)
        {
            return rounds.LastOrDefault(backup =>
                backup.round == round && string.IsNullOrEmpty(backup.deleted_at)
            );
        }
    }
}
