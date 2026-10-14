using FiveStack.Entities.Practice;
using SwiftlyS2.Shared.Players;

namespace UtilityPractice;

public partial class UtilityPracticePlugin
{
    private sealed class ShownLineups
    {
        public required int Tick;
        public required IReadOnlyList<LineupRecord> Lineups;
    }

    private readonly Dictionary<ulong, ShownLineups> _shown = new();

    // The spot watcher's own pace. Everything that asks faster than this is
    // asking about the same library through the same filters.
    private const int ShownEveryTicks = 16;

    /// <summary>
    /// What exists for a player right now: their library after their filters.
    /// The markers are drawn from it and "is there a lineup here" is answered
    /// from it, so a spot they filtered out is neither drawn nor walked into.
    ///
    /// Whatever they have loaded or are being drilled on is always in it. A
    /// filter chooses what to look for, and must never take away the throw
    /// they are in the middle of.
    /// </summary>
    private IReadOnlyList<LineupRecord> Shown(ulong steamId)
    {
        if (
            _shown.TryGetValue(steamId, out ShownLineups? cached)
            && _aimTick >= cached.Tick
            && _aimTick - cached.Tick < ShownEveryTicks
        )
        {
            return cached.Lineups;
        }

        List<LineupRecord> lineups = Narrowed(steamId);

        foreach (
            LineupRecord? held in new[]
            {
                _system.StateFor(steamId).Loaded,
                _drill.Current(steamId),
            }
        )
        {
            if (held != null && !lineups.Any(lineup => lineup.client_id == held.client_id))
            {
                lineups.Add(held);
            }
        }

        _shown[steamId] = new ShownLineups { Tick = _aimTick, Lineups = lineups };

        return lineups;
    }

    // A chip was pressed, a target picked or the filters reset: the answer
    // above is stale now rather than in a quarter of a second, and the world
    // should follow the click instead of the next tick of a timer.
    private void FiltersChanged(IPlayer player)
    {
        ulong steamId = player.SteamID;

        _shown.Remove(steamId);
        _standingIn.Remove(steamId);
        _replay.Unhide(steamId);
        ShowWorldFor(steamId);
    }

    private bool Filtering(ulong steamId)
    {
        MenuState menu = MenuFor(steamId);

        return menu.Side != "all"
            || menu.Type != "all"
            || menu.Scope != "all"
            || _target.ContainsKey(steamId);
    }

    private void ResetFilters(IPlayer player)
    {
        MenuState menu = MenuFor(player.SteamID);

        menu.Side = "all";
        menu.Type = "all";
        menu.Scope = "all";
        menu.Page = 0;
        ClearFilters(player.SteamID);
        FiltersChanged(player);
    }
}
