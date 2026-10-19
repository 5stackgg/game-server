using FiveStack.Entities;
using FiveStack.Enums;

namespace FiveStack.Utilities
{
    public static class RoundRestoreGateUtility
    {
        // The api reads this off the RCON reply to tell a refused restore_round
        // from one that was sent on.
        public const string RefusedPrefix = "restore_round refused:";

        // Same for match_state, which the api sends to restart a map from warmup.
        public const string MatchStateRefusedPrefix = "match_state refused:";

        public static string? Refusal(
            bool restoresDisabled,
            bool alreadyRestoring,
            int round,
            bool hasBackup,
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

            if (!hasBackup)
            {
                return $"the server has no backup of round {round}";
            }

            if (connectedPlayers < expectedPlayers)
            {
                return $"waiting for all players to reconnect ({connectedPlayers}/{expectedPlayers})";
            }

            return null;
        }

        // A map's backups are appended as rounds are played, so a round played
        // again after a restore is listed twice and the newer backup is the one
        // that matches what was replayed. A round the api marked deleted was
        // undone by a restore, and stays loadable until a round is played again.
        public static BackupRound? LatestBackup(IEnumerable<BackupRound> rounds, int round)
        {
            return rounds.LastOrDefault(backup =>
                backup.round == round && !string.IsNullOrEmpty(backup.backup_file)
            );
        }

        // A server that reloads mid-map offers to restore its newest backup, which
        // must be a round that was kept, not one a restore or a map restart undid.
        public static bool IsUndone(IEnumerable<BackupRound> rounds, int round)
        {
            return !string.IsNullOrEmpty(
                rounds.LastOrDefault(backup => backup.round == round)?.deleted_at
            );
        }

        public static string? WarmupRefusal(bool matchLoaded, eMapStatus current)
        {
            if (!matchLoaded)
            {
                return "no match is loaded";
            }

            if (
                current == eMapStatus.WaitingForTV
                || current == eMapStatus.UploadingDemo
                || current == eMapStatus.Finished
                || current == eMapStatus.Surrendered
            )
            {
                return "the map is already over";
            }

            return null;
        }
    }
}
