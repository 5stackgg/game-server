using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Sounds;

namespace UtilityPractice;

// What happens the moment the panel scores a throw. The verdict goes beside
// the grenade on the list, into the ear, and into the five dots on the throw
// card; a drill that ends is summed up on the right, where its plan was.
public partial class UtilityPracticePlugin
{
    private class RunTally
    {
        public readonly List<string> Mastered = new();
        public readonly Dictionary<string, int> Misses = new();
    }

    // The throw each player last made at a loaded lineup, waiting for its
    // score. Only the latest: an older one has already been answered.
    private readonly Dictionary<ulong, (LineupRecord Loaded, LineupRecord Thrown)> _attempts = new();

    // Landed or not, oldest first, per player and lineup.
    private readonly Dictionary<(ulong steamId, string lineup), List<bool>> _lastThrows = new();

    private readonly Dictionary<ulong, RunTally> _runTally = new();
    private readonly Dictionary<ulong, (NadeBarTemplate.Card Card, int Until)> _summaries = new();

    // Played to the thrower alone. A Panorama style's sound only plays on
    // hover, never when the server sets a class.
    private readonly HashSet<ulong> _soundsOff = new();

    private const string LandedSound = "UI.XP.Star.Full";
    private const string MissedSound = "UI.CounterBeep";
    private const string MasteredSound = "UI.XP.LevelUp";
    private const int SummaryTicks = 10 * 64;

    private static string ScoreKey(LineupRecord lineup)
    {
        return PracticeLineupUtility.IsPanelId(lineup.id) ? lineup.id! : lineup.client_id;
    }

    private void OnAttempted(ulong steamId, LineupRecord loaded, LineupRecord thrown)
    {
        _attempts[steamId] = (loaded, thrown);
    }

    private void OnThrowScored(ulong steamId, string lineupId, UtilityPracticeResult? result)
    {
        LiveUtility? entry = LiveScored(steamId, lineupId, result);

        if (
            !_attempts.TryGetValue(steamId, out (LineupRecord Loaded, LineupRecord Thrown) attempt)
            || ScoreKey(attempt.Loaded) != lineupId
        )
        {
            return;
        }

        _attempts.Remove(steamId);

        if (result == null)
        {
            return;
        }

        LineupRecord loaded = attempt.Loaded;
        int bar = _drill.MasteryStreak;
        bool newlyMastered = result.mastered_at != null && result.current_streak == bar;

        if (entry != null)
        {
            entry.Timing = PracticeThrowTiming.Describe(
                PracticeThrowTiming.ReleaseOffset(attempt.Thrown, loaded)
            );
        }

        if (!_lastThrows.TryGetValue((steamId, lineupId), out List<bool>? last))
        {
            last = new List<bool>();
            _lastThrows[(steamId, lineupId)] = last;
        }

        last.Add(result.success);

        if (last.Count > HudSlots.LastThrows)
        {
            last.RemoveAt(0);
        }

        IPlayer? player = _system.Find(steamId);

        if (player != null && player.IsValid)
        {
            PlayVerdict(player, newlyMastered ? MasteredSound : result.success ? LandedSound : MissedSound);
        }

        if (_drill.Current(steamId) == null)
        {
            return;
        }

        if (!_runTally.TryGetValue(steamId, out RunTally? tally))
        {
            tally = new RunTally();
            _runTally[steamId] = tally;
        }

        string name = PracticeLineupUtility.TitleCase(DrillUtility.Name(loaded));

        if (newlyMastered && !tally.Mastered.Contains(name))
        {
            tally.Mastered.Add(name);
        }

        if (!result.success)
        {
            tally.Misses[name] = tally.Misses.GetValueOrDefault(name) + 1;
        }
    }

    private void OnDrillFinished(ulong steamId, PracticeDrillRun run)
    {
        _runTally.Remove(steamId, out RunTally? tally);

        if (run.Attempts == 0)
        {
            return;
        }

        var rows = new List<NadeBarTemplate.CardRow>();

        foreach (string name in tally?.Mastered ?? new List<string>())
        {
            rows.Add(new NadeBarTemplate.CardRow(name, "mastered", Done: true));
        }

        if (tally != null && tally.Misses.Count > 0)
        {
            KeyValuePair<string, int> worst = tally.Misses.OrderByDescending(pair => pair.Value).First();

            rows.Add(new NadeBarTemplate.CardRow(worst.Key, $"missed {worst.Value}×"));
        }

        _summaries[steamId] = (
            new NadeBarTemplate.Card(
                run.Endless ? "Repeat done" : "Drill done",
                $"{run.Attempts} throws · {run.Hits} landed · {run.Hits * 100 / run.Attempts}%",
                $"Best run {run.BestStreak} in a row",
                rows.Take(HudSlots.PlanRows).ToList()
            ),
            _aimTick + SummaryTicks
        );
    }

    private NadeBarTemplate.Card? SummaryCard(ulong steamId)
    {
        if (!_summaries.TryGetValue(steamId, out (NadeBarTemplate.Card Card, int Until) summary))
        {
            return null;
        }

        if (_aimTick >= summary.Until)
        {
            _summaries.Remove(steamId);

            return null;
        }

        return summary.Card;
    }

    private void PlayVerdict(IPlayer player, string sound)
    {
        if (_soundsOff.Contains(player.SteamID))
        {
            return;
        }

        try
        {
            using var play = new SoundEvent(sound);

            play.Recipients = CRecipientFilter.FromPlayers(player.Slot);
            play.Emit();
        }
        catch (Exception error)
        {
            _logger.LogWarning(error, "unable to play {sound}", sound);
        }
    }

    private IReadOnlyList<bool> LastFor(ulong steamId, LineupRecord lineup)
    {
        return _lastThrows.TryGetValue((steamId, ScoreKey(lineup)), out List<bool>? last)
            ? last
            : Array.Empty<bool>();
    }

    private void ForgetVerdicts(ulong steamId)
    {
        _soundsOff.Remove(steamId);
        _attempts.Remove(steamId);
        _runTally.Remove(steamId);
        _summaries.Remove(steamId);
        _lastThrows.Keys.Where(key => key.steamId == steamId).ToList().ForEach(key => _lastThrows.Remove(key));
    }

    private void ForgetEveryVerdict()
    {
        _attempts.Clear();
        _runTally.Clear();
        _summaries.Clear();
    }
}
