using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FiveStack.Entities;
using FiveStack.Enums;

namespace FiveStack.Utilities;

public class OfflineRoundProgress
{
    public int round { get; set; }
    public string deleted_at { get; set; } = "";
}

public class OfflineMapProgress
{
    public Guid id { get; set; }
    public string status { get; set; } = eMapStatus.Unknown.ToString();
    public Guid? winning_lineup_id { get; set; }
    public string lineup_1_side { get; set; } = "";
    public string lineup_2_side { get; set; } = "";
    public List<OfflineRoundProgress> rounds { get; set; } = new();
}

public class OfflineMatchProgress
{
    public Guid match_id { get; set; }
    public string source_hash { get; set; } = "";
    public Guid? current_match_map_id { get; set; }
    public string match_status { get; set; } = "";
    public Guid? winning_lineup_id { get; set; }
    public List<OfflineMapProgress> maps { get; set; } = new();
}

public readonly record struct OfflineMatchResult(eMapStatus Status, Guid? WinningLineupId);

// Offline there is no api to keep what a match has played, so the server keeps
// it in memory and on the demos mount: the game directory is lost on a restart.
public static class OfflineMatchState
{
    public const string DemosRoot = "/opt/demos";

    public const string FileName = "offline-state.json";

    public const string Tie = "Tie";

    public static string Hash(string matchJson)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(matchJson)));
    }

    public static string Folder(string demosRoot, Guid matchId)
    {
        return Path.Join(demosRoot, matchId.ToString());
    }

    // The match JSON is only ever changed by an edit in the offline tool, and the
    // connector restarts the server for one. Read again, it says map 1 in warmup.
    public static bool KeepsLoaded(bool loaded, string? loadedHash, string sourceHash)
    {
        return loaded && loadedHash == sourceHash;
    }

    public static bool IsOver(eMapStatus status)
    {
        return status == eMapStatus.WaitingForTV
            || status == eMapStatus.UploadingDemo
            || status == eMapStatus.Finished
            || status == eMapStatus.Surrendered;
    }

    public static eMapStatus StatusOf(MatchMap map)
    {
        return Enum.TryParse(map.status, out eMapStatus status) ? status : eMapStatus.Unknown;
    }

    // What the api records for a map status. A surrender stands: it ends the
    // match, and the statuses after it only wind the map down. A map that is
    // over never goes back into play, or its result would stop counting.
    public static void Record(MatchMap map, eMapStatus status, Guid? winningLineupId)
    {
        eMapStatus recorded = StatusOf(map);

        if (recorded == eMapStatus.Surrendered || (IsOver(recorded) && !IsOver(status)))
        {
            return;
        }

        map.status = status.ToString();

        if (winningLineupId != null && IsOver(status))
        {
            map.winning_lineup_id = winningLineupId;
        }
    }

    // Level scores win nobody the map, as in the api's series count.
    public static Guid? MapWinner(MatchData match, int lineup1Score, int lineup2Score)
    {
        if (lineup1Score > lineup2Score)
        {
            return match.lineup_1_id;
        }

        if (lineup2Score > lineup1Score)
        {
            return match.lineup_2_id;
        }

        return null;
    }

    public static int MapsToWin(int bestOf)
    {
        return (int)Math.Ceiling(bestOf / 2.0);
    }

    // The api's update_match_state, plus a surrender, which ends the whole match.
    public static OfflineMatchResult? Result(MatchData match)
    {
        MatchMap? surrendered = match.match_maps.FirstOrDefault(map =>
            StatusOf(map) == eMapStatus.Surrendered && map.winning_lineup_id != null
        );

        if (surrendered != null)
        {
            return new OfflineMatchResult(eMapStatus.Surrendered, surrendered.winning_lineup_id);
        }

        MatchMap[] decided = match.match_maps.Where(map => IsOver(StatusOf(map))).ToArray();
        int lineup1Wins = decided.Count(map => map.winning_lineup_id == match.lineup_1_id);
        int lineup2Wins = decided.Count(map => map.winning_lineup_id == match.lineup_2_id);
        int mapsToWin = MapsToWin(match.options.best_of);

        if (lineup1Wins >= mapsToWin || lineup2Wins >= mapsToWin)
        {
            return new OfflineMatchResult(
                eMapStatus.Finished,
                lineup1Wins > lineup2Wins ? match.lineup_1_id : match.lineup_2_id
            );
        }

        if (match.match_maps.Length == 0 || decided.Length < match.match_maps.Length)
        {
            return null;
        }

        if (lineup1Wins == lineup2Wins)
        {
            return new OfflineMatchResult(eMapStatus.Finished, null);
        }

        return new OfflineMatchResult(
            eMapStatus.Finished,
            lineup1Wins > lineup2Wins ? match.lineup_1_id : match.lineup_2_id
        );
    }

    public static bool IsMatchOver(MatchData match)
    {
        return Result(match) != null;
    }

    public static MatchMap? NextMap(MatchData match, MatchMap current)
    {
        if (IsMatchOver(match))
        {
            return null;
        }

        return match.match_maps.FirstOrDefault(map => map.order == current.order + 1);
    }

    // Moves the match on to its next map, or leaves it on this one when the
    // series is over.
    public static MatchMap? Advance(MatchData match, MatchMap current)
    {
        MatchMap? next = NextMap(match, current);

        if (next == null)
        {
            return null;
        }

        match.current_match_map_id = next.id;
        next.status = eMapStatus.Warmup.ToString();

        return next;
    }

    // The api's reset_match_map_to_round: it refuses what it would refuse, and
    // marks the rounds after the one being loaded undone, so a restart can't
    // offer them back. The api forgets an undone round once the next round is
    // played, and nothing does that here, so an undone round is refused outright.
    public static string? ResetToRound(MatchMap map, bool isRush, int round, string now)
    {
        if (isRush && round > 0)
        {
            return "Rush matches only reset to round 0";
        }

        eMapStatus status = StatusOf(map);

        if (!RoundRestoreGateUtility.IsInPlay(status))
        {
            return $"the map is {status}, not in play";
        }

        if (round > 0 && !map.rounds.Any(backup => backup.round == round))
        {
            return $"there is no round {round} on this map to go back to";
        }

        if (round > 0 && RoundRestoreGateUtility.IsUndone(map.rounds, round))
        {
            return $"round {round} was undone by an earlier restore";
        }

        map.rounds = map
            .rounds.Select(backup => new BackupRound
            {
                round = backup.round,
                backup_file = backup.backup_file,
                deleted_at = backup.round > round ? now : "",
            })
            .ToArray();

        return null;
    }

    public static OfflineMatchProgress Capture(MatchData match, string sourceHash)
    {
        OfflineMatchResult? result = Result(match);

        return new OfflineMatchProgress
        {
            match_id = match.id,
            source_hash = sourceHash,
            current_match_map_id = match.current_match_map_id,
            match_status = result == null
                ? ""
                : result.Value.WinningLineupId == null
                    ? Tie
                    : result.Value.Status.ToString(),
            winning_lineup_id = result?.WinningLineupId,
            maps = match
                .match_maps.Select(map => new OfflineMapProgress
                {
                    id = map.id,
                    status = map.status,
                    winning_lineup_id = map.winning_lineup_id,
                    lineup_1_side = map.lineup_1_side,
                    lineup_2_side = map.lineup_2_side,
                    rounds = map
                        .rounds.GroupBy(backup => backup.round)
                        .Select(played => new OfflineRoundProgress
                        {
                            round = played.Key,
                            deleted_at = played.Last().deleted_at,
                        })
                        .OrderBy(played => played.round)
                        .ToList(),
                })
                .ToList(),
        };
    }

    // A map whose end was cut short by the restart is finished here, as its
    // result is already recorded and nothing else would finish it.
    public static bool Restore(MatchData match, OfflineMatchProgress progress, string sourceHash)
    {
        if (progress.match_id != match.id || progress.source_hash != sourceHash)
        {
            return false;
        }

        foreach (MatchMap map in match.match_maps)
        {
            OfflineMapProgress? saved = progress.maps.FirstOrDefault(kept => kept.id == map.id);

            if (saved == null)
            {
                continue;
            }

            map.status = saved.status;
            map.winning_lineup_id = saved.winning_lineup_id;

            if (!string.IsNullOrEmpty(saved.lineup_1_side))
            {
                map.lineup_1_side = saved.lineup_1_side;
            }

            if (!string.IsNullOrEmpty(saved.lineup_2_side))
            {
                map.lineup_2_side = saved.lineup_2_side;
            }
        }

        if (match.match_maps.Any(map => map.id == progress.current_match_map_id))
        {
            match.current_match_map_id = progress.current_match_map_id;
        }

        MatchMap? current = match.match_maps.FirstOrDefault(map =>
            map.id == match.current_match_map_id
        );

        if (current == null)
        {
            return true;
        }

        eMapStatus currentStatus = StatusOf(current);

        if (IsOver(currentStatus) && currentStatus != eMapStatus.Surrendered)
        {
            current.status = eMapStatus.Finished.ToString();
            Advance(match, current);
        }

        return true;
    }

    public static string BackupPrefix(Guid matchId, Guid mapId)
    {
        return $"{matchId}_{mapId}".Replace("-", "");
    }

    public static string BackupFileName(Guid matchId, Guid mapId, int round)
    {
        return $"{BackupPrefix(matchId, mapId)}_round{round.ToString().PadLeft(2, '0')}.txt";
    }

    public static void Save(string folder, OfflineMatchProgress progress)
    {
        Directory.CreateDirectory(folder);

        string path = Path.Join(folder, FileName);
        string temp = path + ".tmp";

        File.WriteAllText(temp, JsonSerializer.Serialize(progress));
        File.Move(temp, path, true);
    }

    public static OfflineMatchProgress? Load(string folder)
    {
        string path = Path.Join(folder, FileName);

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<OfflineMatchProgress>(File.ReadAllText(path));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static int SaveBackups(string gameDirectory, string folder, string prefix)
    {
        if (!Directory.Exists(gameDirectory))
        {
            return 0;
        }

        Directory.CreateDirectory(folder);

        int copied = 0;

        foreach (string source in Directory.GetFiles(gameDirectory, $"{prefix}_round*.txt"))
        {
            string target = Path.Join(folder, Path.GetFileName(source));

            if (
                File.Exists(target)
                && File.GetLastWriteTimeUtc(target) >= File.GetLastWriteTimeUtc(source)
            )
            {
                continue;
            }

            File.Copy(source, target, true);
            copied++;
        }

        return copied;
    }

    // A file the game directory still has is newer than the kept copy, as on a
    // plugin reload, so only missing ones are put back.
    public static int RestoreBackups(string folder, string gameDirectory, Guid matchId)
    {
        if (!Directory.Exists(folder))
        {
            return 0;
        }

        Directory.CreateDirectory(gameDirectory);

        int restored = 0;

        foreach (string source in BackupFiles(folder, matchId))
        {
            string target = Path.Join(gameDirectory, Path.GetFileName(source));

            if (File.Exists(target))
            {
                continue;
            }

            File.Copy(source, target);
            restored++;
        }

        return restored;
    }

    public static void LoadBackups(MatchData match, OfflineMatchProgress progress, string folder)
    {
        foreach (MatchMap map in match.match_maps)
        {
            OfflineMapProgress? saved = progress.maps.FirstOrDefault(kept => kept.id == map.id);
            string prefix = $"{BackupPrefix(match.id, map.id)}_round";
            List<BackupRound> loaded = new();

            foreach (string file in Directory.GetFiles(folder, $"{prefix}*.txt"))
            {
                string number = Path.GetFileNameWithoutExtension(file)[prefix.Length..];

                if (!int.TryParse(number, out int round))
                {
                    continue;
                }

                loaded.Add(
                    new BackupRound
                    {
                        round = round,
                        backup_file = File.ReadAllText(file),
                        deleted_at =
                            saved?.rounds.FirstOrDefault(kept => kept.round == round)?.deleted_at
                            ?? "",
                    }
                );
            }

            map.rounds = map.rounds.Concat(loaded.OrderBy(backup => backup.round)).ToArray();
        }
    }

    // Left behind by an edited match, these would be offered as its rounds.
    public static void Forget(string folder, Guid matchId)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        File.Delete(Path.Join(folder, FileName));

        foreach (string file in BackupFiles(folder, matchId))
        {
            File.Delete(file);
        }
    }

    private static string[] BackupFiles(string folder, Guid matchId)
    {
        return Directory.GetFiles(folder, $"{matchId.ToString().Replace("-", "")}_*_round*.txt");
    }
}
