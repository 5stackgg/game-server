using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;

namespace UtilityPractice;

// What was just thrown: every grenade in the air or recently gone off, how
// long it was in the air, and for a smoke or a fire how long it has left. The
// game shows a player none of this, and "when does that smoke fade" is half of
// what an execute is timed around. A throw made at a lineup also says how it
// scored, next to the grenade it is about rather than in chat.
public partial class UtilityPracticePlugin
{
    private class LiveUtility
    {
        public required uint Index;
        public required ulong Owner;

        // RadarMaps' name for it: smoke, molly, flash, he or decoy.
        public required string Kind;
        public required string Name;
        public required int Thrown;
        public string? Color;
        public int Popped = -1;
        public Vec3 At;
        public bool Scored;
        public UtilityPracticeResult? Result;

        // How far off a jump throw's release was, when it was far enough to say.
        public string? Timing;

        public bool Smoke => Kind == "smoke";

        // A smoke and a fire stay where they land; the rest are over as they go off.
        public bool Lingers => Kind is "smoke" or "molly";
    }

    private readonly List<LiveUtility> _live = new();
    private readonly HashSet<ulong> _outOff = new();

    // How long each lasts once it has gone off. Starting guesses only: the
    // game says when one ends, and whatever it says replaces these, so the
    // bar is right on any server whatever its cvars are set to.
    private int _smokeLifeTicks = 18 * 64;
    private int _fireLifeTicks = 7 * 64;

    // A flash, an HE or a decoy stays on the list as long as a smoke would:
    // long enough to read how it went, then out of the way.
    private const int ShownTicks = 18 * 64;

    private const int LifeLeastTicks = 2 * 64;
    private const int LifeMostTicks = 40 * 64;

    // A grenade still in the air after this was lost: out of the map, or
    // cleared from under the list.
    private const int AirMostTicks = 20 * 64;

    // The end of a fire carries where it was and nothing about who threw it.
    private const float FireMatchUnits = 300f;

    private const int FadingTicks = 3 * 64;

    private static string? LiveKind(string designer)
    {
        return designer switch
        {
            "smokegrenade_projectile" => "smoke",
            "molotov_projectile" => "molly",
            "flashbang_projectile" => "flash",
            "hegrenade_projectile" => "he",
            "decoy_projectile" => "decoy",
            _ => null,
        };
    }

    private static string KindName(string kind)
    {
        return kind switch
        {
            "smoke" => "Smoke",
            "molly" => "Molotov",
            "flash" => "Flash",
            "he" => "HE",
            _ => "Decoy",
        };
    }

    private static string KindType(string kind)
    {
        return kind switch
        {
            "smoke" => "Smoke",
            "molly" => "Molotov",
            "flash" => "Flash",
            "he" => "HighExplosive",
            _ => "Decoy",
        };
    }

    private void TrackLive(IPlayer player, uint index, string designer)
    {
        string? kind = LiveKind(designer);

        if (kind == null)
        {
            return;
        }

        ulong steamId = player.SteamID;
        PracticeState state = _system.StateFor(steamId);
        LineupRecord? loaded = state.Loaded;

        // A rethrow knows what it is. A throw made with a lineup loaded is
        // taken to be an attempt at it, which is what it nearly always is.
        string name =
            _replay.GhostName(index)
            ?? (
                loaded != null && loaded.utility_type == KindType(kind)
                    ? DrillUtility.Name(loaded)
                    : KindName(kind)
            );

        _live.RemoveAll(entry => entry.Index == index);
        _live.Add(
            new LiveUtility
            {
                Index = index,
                Owner = steamId,
                Kind = kind,
                Name = PracticeLineupUtility.TitleCase(name),
                Thrown = _aimTick,
                Color = kind == "smoke" && state.Colors ? InFlightColor(steamId).Name : null,
            }
        );
    }

    // Everything but a molotov names itself when it goes off.
    private void LivePopped(uint index, Vec3 at)
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
            .Where(candidate => candidate.Kind == "molly" && candidate.Owner == steamId && candidate.Popped < 0)
            .OrderBy(candidate => candidate.Thrown)
            .FirstOrDefault();

        if (entry != null)
        {
            entry.Popped = _aimTick;
            entry.At = at;
        }
    }

    // The panel's verdict on a throw arrives after the grenade has gone off,
    // so it goes on the newest of the thrower's grenades still waiting for one.
    private LiveUtility? LiveScored(ulong steamId, string lineupId, UtilityPracticeResult? result)
    {
        LiveUtility? entry = _live
            .Where(candidate => candidate.Owner == steamId && candidate.Popped >= 0 && !candidate.Scored)
            .OrderByDescending(candidate => candidate.Popped)
            .FirstOrDefault();

        if (entry != null)
        {
            entry.Scored = true;
            entry.Result = result;
        }

        return entry;
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
            .Where(candidate => candidate.Kind == "molly" && candidate.Popped >= 0)
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
        return entry.Kind switch
        {
            "smoke" => _smokeLifeTicks,
            "molly" => _fireLifeTicks,
            _ => ShownTicks,
        };
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
                : _aimTick - entry.Popped > LifeOf(entry) + (entry.Lingers ? FadingTicks : 0)
        );
    }

    // A grenade is first seen a tick after it leaves the hand, which is the
    // tick this counts from; the one before it is added back.
    private static string Seconds(int ticks)
    {
        return $"{(ticks + 1) / 64f:0.0}s";
    }

    // How the throw scored, short enough to sit under its name.
    private string ScoreLine(LiveUtility entry)
    {
        if (!entry.Scored)
        {
            return "";
        }

        UtilityPracticeResult? result = entry.Result;

        if (result == null)
        {
            return "not scored";
        }

        string off = PracticeLineupUtility.Metres(result.distance);

        string verdict =
            !result.success ? $"missed · {off} off"
            : result.mastered_at != null ? $"landed · {off} off · mastered"
            : $"landed · {off} off";

        return entry.Timing != null ? $"{verdict} · {entry.Timing}" : verdict;
    }

    private (IReadOnlyList<NadeBarTemplate.Out> rows, int total) OutsFor(ulong viewer)
    {
        if (_live.Count == 0 || _outOff.Contains(viewer))
        {
            return (Array.Empty<NadeBarTemplate.Out>(), 0);
        }

        var rows = new List<NadeBarTemplate.Out>();

        // Newest first: the throw just made is the one being looked for.
        foreach (LiveUtility entry in _live.AsEnumerable().Reverse().Take(HudSlots.OutRows))
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

            bool? landed = entry.Scored && entry.Result != null ? entry.Result.success : null;

            if (entry.Popped < 0)
            {
                rows.Add(
                    new NadeBarTemplate.Out(
                        name,
                        $"in the air {Seconds(_aimTick - entry.Thrown)}",
                        "",
                        "",
                        entry.Kind,
                        entry.Color,
                        HudSlots.OutFillSteps - 1,
                        Air: true,
                        Fading: false,
                        Landed: null
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
                    $"air {Seconds(entry.Popped - entry.Thrown)}",
                    ScoreLine(entry),
                    entry.Lingers ? $"{(left + 63) / 64}s" : "",
                    entry.Kind,
                    entry.Color,
                    (left * steps + life - 1) / life,
                    Air: false,
                    Fading: left <= FadingTicks,
                    Landed: landed
                )
            );
        }

        return (rows, _live.Count);
    }
}
