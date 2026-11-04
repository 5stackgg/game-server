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

        // A restore vote leaves the round running, because mp_pause_match waits for
        // the next freezetime, so a round ending during one is real and is lost if
        // the vote fails. A recovery vote is not that: the server is behind the
        // backend, and anything it reported would land over rounds already kept.
        public static bool HoldsRoundResults(bool resetting, bool voteOpen, bool recovering)
        {
            return resetting && (!voteOpen || recovering);
        }

        // Only a server behind the backend has anything to protect, and offline
        // nothing would ever answer.
        public static bool OwesLoad(bool recovering, bool offline)
        {
            return recovering && !offline;
        }

        // The api refuses a round it has no row for and tells nobody, so a backup
        // file of one can never load. Offline there is no api to refuse it.
        public static bool IsUsableOnDisk(IEnumerable<BackupRound> rounds, int round, bool offline)
        {
            return !IsUndone(rounds, round)
                && (offline || rounds.Any(backup => backup.round == round));
        }

        // No round has ended to carry round 0's backup to the api, so only the
        // server playing the map has it, on disk. Out of play that file can be
        // warmup's, or gone with a map being played again.
        public static bool ReadsBackupFromDisk(int round, eMapStatus status)
        {
            return round == 0 && IsInPlay(status);
        }

        // .stop goes back to the start of the round being played. Once a round is
        // over that is the round that just ended, as the next one's backup is not
        // written until it starts.
        public static int StopRound(int totalRoundsPlayed, bool roundOver)
        {
            return Math.Max(0, roundOver ? totalRoundsPlayed - 1 : totalRoundsPlayed);
        }

        public const string RestartMapNeeded =
            " No round can be restored, an admin needs to use Restart Map.";

        // Where a hold goes when it comes back, as a hold on a round that can't
        // load would come back for ever. The newest round the backend can load, or
        // NothingToLoad if it has none. Null when no recorded round is ahead of the
        // server: there is nothing left to play over.
        public static int? RoundToHold(
            eMapStatus status,
            IEnumerable<BackupRound> rounds,
            int round,
            int totalRoundsPlayed
        )
        {
            if (!IsInPlay(status))
            {
                return null;
            }

            if (LatestBackup(rounds, round) != null)
            {
                return round;
            }

            if (HighestRecordedRound(rounds, 0) <= totalRoundsPlayed)
            {
                return null;
            }

            return HighestRestorableRound(rounds, 0);
        }

        public static string WhyTheHoldMoved(int round, int? next)
        {
            return next == RoundRestoreState.NothingToLoad
                ? RestartMapNeeded
                : $" Round {round} can't be restored, its backup is gone.";
        }

        public static string? ResumeRefusal(int? owedRound, bool needsRestartMap = false)
        {
            if (needsRestartMap)
            {
                return RestartMapNeeded;
            }

            return owedRound == null
                ? null
                : $" Round {owedRound} is still being restored, the match can't resume yet.";
        }

        public static string? Refusal(
            bool restoresDisabled,
            bool alreadyRestoring,
            eMapStatus mapStatus,
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

            // CS2 restarts the game when warmup or knife ends, which would wipe a
            // round loaded before that, so the api ignores a restore until then.
            if (mapStatus == eMapStatus.Warmup || mapStatus == eMapStatus.Knife)
            {
                return "the map isn't live yet";
            }

            if (IsOver(mapStatus))
            {
                return "the map is already over";
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

        // Every stat row the api keeps for a round comes from one of these. A
        // backup load outlasts what the api clears when it restores a round, so
        // whatever the engine reports while it runs would stay in the stats.
        private static readonly HashSet<string> RoundStatEvents = new()
        {
            "kill",
            "assist",
            "damage",
            "utility",
            "flash",
            "objective",
        };

        public static bool DroppedDuringLoad(string gameEvent, bool loadingBackup)
        {
            return loadingBackup && RoundStatEvents.Contains(gameEvent);
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

        // The newest round a server that reloaded mid-map can offer: the higher of
        // its own backup files and the backups the api kept. A server that came
        // back on another node or a fresh pod has only the api's.
        public static int HighestRestorableRound(IEnumerable<BackupRound> rounds, int highestOnDisk)
        {
            int kept = rounds
                .Where(backup =>
                    !string.IsNullOrEmpty(backup.backup_file) && !IsUndone(rounds, backup.round)
                )
                .Select(backup => backup.round)
                .DefaultIfEmpty(0)
                .Max();

            return Math.Max(highestOnDisk, kept);
        }

        // Any round recorded past the server, restorable or not: a server behind
        // with nothing to restore plays the map again from 0-0 rather than over
        // the rounds the backend has.
        public static int HighestRecordedRound(IEnumerable<BackupRound> rounds, int highestOnDisk)
        {
            int kept = rounds
                .Where(backup => !IsUndone(rounds, backup.round))
                .Select(backup => backup.round)
                .DefaultIfEmpty(0)
                .Max();

            return Math.Max(highestOnDisk, kept);
        }

        // Only a map being played is held for a restore: one that is over refuses
        // it, and would hold forever.
        public static bool IsInPlay(eMapStatus status)
        {
            return status == eMapStatus.Live
                || status == eMapStatus.Overtime
                || status == eMapStatus.Paused;
        }

        public static string? WarmupRefusal(bool matchLoaded, eMapStatus current)
        {
            if (!matchLoaded)
            {
                return "no match is loaded";
            }

            if (IsOver(current))
            {
                return "the map is already over";
            }

            return null;
        }

        private static bool IsOver(eMapStatus status)
        {
            return status == eMapStatus.WaitingForTV
                || status == eMapStatus.UploadingDemo
                || status == eMapStatus.Finished
                || status == eMapStatus.Surrendered;
        }
    }
}
