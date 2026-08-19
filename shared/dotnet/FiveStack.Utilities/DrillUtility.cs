using FiveStack.Entities.Practice;
using FiveStack.Enums;

namespace FiveStack.Utilities;

// The choosing half of a drill: what can be drilled, in what order, and how
// many of them make a run. Pure, so the runner in each plugin is only the part
// that teleports people and prints.
public static class DrillUtility
{
    public const int DefaultCount = 10;

    // A run is meant to end. Nobody finishes two hundred throws in one sitting,
    // and a queue that long is a way to leave a practice server teleporting
    // somebody all afternoon.
    public const int MaxCount = 50;

    // How long a throw has to come back scored before the run writes it off.
    // The panel's own request timeout is ten seconds and a grenade can be in
    // the air for five, so anything shorter calls a slow answer a lost one.
    public const int ScoreWaitSeconds = 20;

    // Consecutive throws the panel did not answer before the run gives up.
    // Scoring is the drill; standing somebody on lineup after lineup that never
    // resolves is worse than telling them it cannot be scored right now.
    public const int MaxUnscoredInARow = 3;

    // Consecutive lineups that could not be stood on before the run gives up.
    public const int MaxUnloadableInARow = 3;

    // A lineup nobody has thrown yet sorts as though it were half landed: more
    // worth drilling than one that never misses, less than one that keeps
    // missing.
    public const float UnattemptedPriority = 0.5f;

    // Above every rate, so a mastered lineup is always the last thing a
    // worst-first run reaches for.
    private const float MasteredPriority = 2f;

    // Scoring is what makes a throw a drill attempt, and the panel scores by
    // lineup id, so a lineup it has never seen cannot be drilled. The two
    // positions are the other half: a row that arrived without an origin or a
    // landing point cannot be stood on or measured against.
    public static bool IsDrillable(LineupRecord? lineup)
    {
        return lineup != null
            && !string.IsNullOrEmpty(lineup.id)
            && lineup.release.feet_position.Length() > 0f
            && lineup.detonation_position.Length() > 0f;
    }

    public static List<LineupRecord> Drillable(IEnumerable<LineupRecord>? lineups)
    {
        return lineups == null ? new List<LineupRecord>() : lineups.Where(IsDrillable).ToList();
    }

    public static string Name(LineupRecord lineup)
    {
        return string.IsNullOrWhiteSpace(lineup.name) ? lineup.utility_type : lineup.name;
    }

    public static float Priority(DrillProgress? progress)
    {
        if (progress == null || progress.Attempts == 0)
        {
            return UnattemptedPriority;
        }

        return progress.Mastered ? MasteredPriority : progress.Rate;
    }

    // Worst first, and among equally bad ones the one with the most evidence
    // behind it: 0/10 is a more certain problem than 0/1.
    public static List<LineupRecord> WorstFirst(
        IEnumerable<LineupRecord> lineups,
        Func<LineupRecord, DrillProgress?> progressFor
    )
    {
        return lineups
            .OrderBy(lineup => Priority(progressFor(lineup)))
            .ThenByDescending(lineup => progressFor(lineup)?.Attempts ?? 0)
            .ThenBy(lineup => Name(lineup), StringComparer.OrdinalIgnoreCase)
            .ThenBy(lineup => lineup.client_id, StringComparer.Ordinal)
            .ToList();
    }

    public static List<LineupRecord> Shuffled(IEnumerable<LineupRecord> lineups, Random random)
    {
        List<LineupRecord> shuffled = lineups.ToList();

        for (int index = shuffled.Count - 1; index > 0; index--)
        {
            int swap = random.Next(index + 1);
            (shuffled[index], shuffled[swap]) = (shuffled[swap], shuffled[index]);
        }

        return shuffled;
    }

    // The run, in the order it will be handed out. A book shorter than the run
    // is drilled in whole passes rather than by picking each throw on its own,
    // so nothing comes round twice before everything has come round once.
    public static List<LineupRecord> Queue(
        IEnumerable<LineupRecord>? library,
        int count,
        eDrillOrder order,
        Func<LineupRecord, DrillProgress?> progressFor,
        Random random
    )
    {
        List<LineupRecord> drillable = Drillable(library);
        var queue = new List<LineupRecord>();

        if (drillable.Count == 0)
        {
            return queue;
        }

        count = Math.Clamp(count, 1, MaxCount);

        while (queue.Count < count)
        {
            List<LineupRecord> pass =
                order == eDrillOrder.Worst
                    ? WorstFirst(drillable, progressFor)
                    : Shuffled(drillable, random);

            // The seam between two passes is the one place a shuffle can hand
            // out the same lineup twice in a row.
            if (
                queue.Count > 0
                && pass.Count > 1
                && string.Equals(pass[0].client_id, queue[^1].client_id, StringComparison.Ordinal)
            )
            {
                (pass[0], pass[1]) = (pass[1], pass[0]);
            }

            foreach (LineupRecord lineup in pass)
            {
                if (queue.Count >= count)
                {
                    break;
                }

                queue.Add(lineup);
            }
        }

        return queue;
    }

    // ".drill", ".drill 20", ".drill worst", ".drill 20 worst", ".drill stop".
    // Order and count are read wherever they appear rather than by position: a
    // player typing this into chat mid-round is not consulting a usage line.
    public static DrillRequest Parse(string? argument)
    {
        var request = new DrillRequest();

        string[] tokens = (argument ?? "")
            .Trim()
            .Trim('"')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (string token in tokens)
        {
            if (
                token.Equals("stop", StringComparison.OrdinalIgnoreCase)
                || token.Equals("end", StringComparison.OrdinalIgnoreCase)
            )
            {
                request.Stop = true;
                continue;
            }

            if (token.Equals("worst", StringComparison.OrdinalIgnoreCase))
            {
                request.Order = eDrillOrder.Worst;
                continue;
            }

            if (token.Equals("random", StringComparison.OrdinalIgnoreCase))
            {
                request.Order = eDrillOrder.Random;
                continue;
            }

            if (int.TryParse(token, out int count) && count > 0)
            {
                request.Count = Math.Min(count, MaxCount);
                continue;
            }

            request.Valid = false;
        }

        return request;
    }
}

// What a player asked .drill for.
public class DrillRequest
{
    public bool Stop { get; set; }
    public bool Valid { get; set; } = true;
    public eDrillOrder Order { get; set; } = eDrillOrder.Random;
    public int Count { get; set; } = DrillUtility.DefaultCount;
}

// What the panel has said about one player and one lineup. Nothing here is
// counted locally: these are the panel's own totals, which is why a result
// replaces them rather than adding to them.
public class DrillProgress
{
    public int Attempts { get; set; }
    public int Successes { get; set; }
    public int BestStreak { get; set; }
    public bool Mastered { get; set; }

    public float Rate => Attempts == 0 ? 0f : (float)Successes / Attempts;

    public static DrillProgress From(NadePracticeResult result)
    {
        return new DrillProgress
        {
            Attempts = result.attempts,
            Successes = result.successes,
            BestStreak = result.best_streak,
            Mastered = result.mastered_at != null,
        };
    }
}

// Everything the panel has told this server about how people are doing, kept
// per steam id so two players drilling the same lineup never read each other's
// numbers.
public class DrillProgressBook
{
    private readonly Dictionary<ulong, Dictionary<string, DrillProgress>> _progress =
        new Dictionary<ulong, Dictionary<string, DrillProgress>>();

    public void Record(ulong steamId, string? lineupId, NadePracticeResult? result)
    {
        if (result == null || string.IsNullOrEmpty(lineupId))
        {
            return;
        }

        if (!_progress.TryGetValue(steamId, out Dictionary<string, DrillProgress>? lineups))
        {
            lineups = new Dictionary<string, DrillProgress>(StringComparer.Ordinal);
            _progress[steamId] = lineups;
        }

        lineups[lineupId] = DrillProgress.From(result);
    }

    public DrillProgress? For(ulong steamId, string? lineupId)
    {
        if (
            string.IsNullOrEmpty(lineupId)
            || !_progress.TryGetValue(steamId, out Dictionary<string, DrillProgress>? lineups)
        )
        {
            return null;
        }

        return lineups.TryGetValue(lineupId, out DrillProgress? progress) ? progress : null;
    }

    public Func<LineupRecord, DrillProgress?> Lookup(ulong steamId)
    {
        return lineup => For(steamId, lineup.id);
    }

    public void Forget(ulong steamId)
    {
        _progress.Remove(steamId);
    }

    public void Clear()
    {
        _progress.Clear();
    }
}
