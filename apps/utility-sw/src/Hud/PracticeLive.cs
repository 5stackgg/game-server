using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;

namespace UtilityPractice;

// What is out: every smoke and fire in the world, and how long each has left.
// The game shows a player none of this, and "when does that smoke fade" is
// half of what an execute is timed around.
//
// Only what stays. A flash or an HE is over as it lands, and a list of things
// that are already gone is not a list anybody reads.
public partial class UtilityPracticePlugin
{
    private class LiveUtility
    {
        public required uint Index;
        public required ulong Owner;
        public required bool Smoke;
        public required string Name;
        public required int Thrown;
        public string? Color;
        public int Popped = -1;
        public Vec3 At;
    }

    private readonly List<LiveUtility> _live = new();
    private readonly HashSet<ulong> _outOff = new();

    // How long each lasts once it has gone off. Starting guesses only: the
    // game says when one ends, and whatever it says replaces these, so the
    // bar is right on any server whatever its cvars are set to.
    private int _smokeLifeTicks = 18 * 64;
    private int _fireLifeTicks = 7 * 64;

    private const int LifeLeastTicks = 2 * 64;
    private const int LifeMostTicks = 40 * 64;

    // A grenade still in the air after this was lost: out of the map, or
    // cleared from under the list.
    private const int AirMostTicks = 20 * 64;

    // The end of a fire carries where it was and nothing about who threw it.
    private const float FireMatchUnits = 300f;

    private const int FadingTicks = 3 * 64;

    private void TrackLive(IPlayer player, uint index, string designer)
    {
        bool smoke = designer == "smokegrenade_projectile";

        if (!smoke && designer != "molotov_projectile")
        {
            return;
        }

        ulong steamId = player.SteamID;
        PracticeState state = _system.StateFor(steamId);
        string type = smoke ? "Smoke" : "Molotov";
        LineupRecord? loaded = state.Loaded;

        // A rethrow knows what it is. A throw made with a lineup loaded is
        // taken to be an attempt at it, which is what it nearly always is.
        string name =
            _replay.GhostName(index)
            ?? (loaded != null && loaded.utility_type == type ? DrillUtility.Name(loaded) : type);

        _live.RemoveAll(entry => entry.Index == index);
        _live.Add(
            new LiveUtility
            {
                Index = index,
                Owner = steamId,
                Smoke = smoke,
                Name = PracticeLineupUtility.TitleCase(name),
                Thrown = _aimTick,
                Color = smoke && state.Colors ? InFlightColor(steamId).Name : null,
            }
        );
    }

    private void LiveSmokePopped(uint index, Vec3 at)
    {
        LiveUtility? entry = _live.FirstOrDefault(candidate => candidate.Index == index);

        if (entry != null && entry.Popped < 0)
        {
            entry.Popped = _aimTick;
            entry.At = at;
        }
    }

    // A molotov going off names its thrower and not itself, so it is taken to
    // be the oldest one that thrower still has in the air.
    private void LiveFirePopped(ulong steamId, Vec3 at)
    {
        LiveUtility? entry = _live
            .Where(candidate => !candidate.Smoke && candidate.Owner == steamId && candidate.Popped < 0)
            .OrderBy(candidate => candidate.Thrown)
            .FirstOrDefault();

        if (entry != null)
        {
            entry.Popped = _aimTick;
            entry.At = at;
        }
    }

    [GameEventHandler(HookMode.Post)]
    public HookResult OnSmokeExpired(EventSmokegrenadeExpired @event)
    {
        uint index = EntityIndex(@event.EntityID);
        LiveUtility? entry = _live.FirstOrDefault(candidate => candidate.Index == index);

        if (entry != null)
        {
            _smokeLifeTicks = Learned(entry, _smokeLifeTicks);
            _live.Remove(entry);
        }

        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Post)]
    public HookResult OnInfernoExpire(EventInfernoExpire @event)
    {
        LiveUtility? entry = FireNear(new Vec3(@event.X, @event.Y, @event.Z));

        if (entry != null)
        {
            _fireLifeTicks = Learned(entry, _fireLifeTicks);
            _live.Remove(entry);
        }

        return HookResult.Continue;
    }

    // Put out by a smoke, which says nothing about how long a fire lasts.
    [GameEventHandler(HookMode.Post)]
    public HookResult OnInfernoExtinguish(EventInfernoExtinguish @event)
    {
        LiveUtility? entry = FireNear(new Vec3(@event.X, @event.Y, @event.Z));

        if (entry != null)
        {
            _live.Remove(entry);
        }

        return HookResult.Continue;
    }

    private LiveUtility? FireNear(Vec3 at)
    {
        return _live
            .Where(candidate => !candidate.Smoke && candidate.Popped >= 0)
            .Select(candidate => (candidate, away: (candidate.At - at).Length()))
            .Where(pair => pair.away <= FireMatchUnits)
            .OrderBy(pair => pair.away)
            .Select(pair => pair.candidate)
            .FirstOrDefault();
    }

    private int Learned(LiveUtility entry, int known)
    {
        if (entry.Popped < 0)
        {
            return known;
        }

        int lasted = _aimTick - entry.Popped;

        return lasted is >= LifeLeastTicks and <= LifeMostTicks ? lasted : known;
    }

    private int LifeOf(LiveUtility entry)
    {
        return entry.Smoke ? _smokeLifeTicks : _fireLifeTicks;
    }

    // Cleared by hand, or lost: the game reports neither.
    private void ForgetLive()
    {
        _live.Clear();
    }

    private void PruneLive()
    {
        _live.RemoveAll(entry =>
            entry.Popped < 0
                ? _aimTick - entry.Thrown > AirMostTicks
                : _aimTick - entry.Popped > LifeOf(entry) + FadingTicks
        );
    }

    private (IReadOnlyList<NadeBarTemplate.Out> rows, int total) OutsFor(ulong viewer)
    {
        if (_live.Count == 0 || _outOff.Contains(viewer))
        {
            return (Array.Empty<NadeBarTemplate.Out>(), 0);
        }

        var rows = new List<NadeBarTemplate.Out>();

        foreach (LiveUtility entry in _live.Take(HudSlots.OutRows))
        {
            string name = entry.Name;

            if (entry.Owner != viewer)
            {
                string? who = _system.Find(entry.Owner)?.Controller.PlayerName;

                if (!string.IsNullOrEmpty(who))
                {
                    name = $"{name} · {who}";
                }
            }

            string type = entry.Smoke ? "smoke" : "molly";

            if (entry.Popped < 0)
            {
                rows.Add(
                    new NadeBarTemplate.Out(
                        name,
                        "IN THE AIR",
                        type,
                        entry.Color,
                        HudSlots.OutFillSteps - 1,
                        Air: true,
                        Fading: false
                    )
                );

                continue;
            }

            int life = LifeOf(entry);
            int left = Math.Max(0, life - (_aimTick - entry.Popped));
            int steps = HudSlots.OutFillSteps - 1;

            rows.Add(
                new NadeBarTemplate.Out(
                    name,
                    $"{(left + 63) / 64}s",
                    type,
                    entry.Color,
                    (left * steps + life - 1) / life,
                    Air: false,
                    Fading: left <= FadingTicks
                )
            );
        }

        return (rows, _live.Count);
    }
}
