using FiveStack.Entities.Practice;

namespace FiveStack.Utilities;

// The scheduling half of an execute: which steps are due, who they belong to,
// and how long the whole thing runs. Pure, so the runner in each plugin is only
// the part that talks to players.
public static class PlaybookUtility
{
    // The panel caps a book at this too. A book that arrived longer than its
    // own contract is a bug somewhere upstream, and running it anyway is how a
    // practice server ends up teleporting people for ten minutes.
    public const int MaxSteps = 32;

    // How long the execute stays live after its last throw, so a step that
    // lands late is still part of the same run.
    public const int TailMs = 3000;

    public static List<NadePlaybookStep> Ordered(NadePlaybook? playbook)
    {
        if (playbook == null)
        {
            return new List<NadePlaybookStep>();
        }

        return playbook
            .steps.Where(step => step.lineup != null)
            .OrderBy(step => step.step_order)
            .ThenBy(step => step.offset_ms)
            .Take(MaxSteps)
            .ToList();
    }

    // Half open on purpose: the cursor a caller keeps is the last elapsed time
    // it has already fired, so passing -1 first fires a step at offset zero
    // exactly once.
    public static List<NadePlaybookStep> Due(
        IReadOnlyList<NadePlaybookStep> steps,
        int afterMs,
        int throughMs
    )
    {
        return steps
            .Where(step => step.offset_ms > afterMs && step.offset_ms <= throughMs)
            .ToList();
    }

    public static int DurationMs(IReadOnlyList<NadePlaybookStep> steps)
    {
        return steps.Count == 0 ? 0 : steps.Max(step => step.offset_ms);
    }

    public static bool IsAssigned(NadePlaybookStep step)
    {
        return !string.IsNullOrWhiteSpace(step.assigned_steam_id);
    }

    // An unassigned step belongs to whoever is standing there, so it prompts
    // everyone rather than nobody.
    public static bool IsFor(NadePlaybookStep step, ulong steamId)
    {
        return !IsAssigned(step)
            || step.assigned_steam_id!.Trim() == steamId.ToString();
    }
}
