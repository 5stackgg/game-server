using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace UtilityPractice;

// Throws a lineup discovery's proposals for real and reports where they
// landed. DiscoveryPlan decides what to throw; this emits it, samples it on
// the tick and hands the landing back.
//
// A discovery runs on a render server nobody plays on, so the grenades are
// owned by a bot it adds for the purpose.
public class PracticeDiscovery
{
    // Grenades in the air at once. Each one released two ticks after the last,
    // so neighbours on similar lines are a grenade's width or more apart.
    private const int MaxInFlight = 24;
    private const int EmitEveryTicks = 2;

    // A grenade that has not settled in this long is stuck in geometry.
    private const int TimeoutTicks = 64 * 12;

    // A smoke pops a moment after it stops; its landing is where it stopped,
    // so a grenade still for this long has landed and its slot is freed early.
    private const int RestTicks = 12;
    private const float RestSpeed = 1f;

    // A bot is asked for at most this often, and given up on after this many
    // asks: a server that will not take one will not throw anything.
    private const int OwnerAskEveryTicks = 64 * 10;
    private const int OwnerAsks = 3;

    // Every fourth tick of a flight is kept for its path.
    private const int PathEveryTicks = 4;
    private const int MaxPathPoints = 256;

    private readonly ISwiftlyCore _core;
    private readonly GrenadeEmitter _emitter;
    private readonly ILogger<PracticeDiscovery> _logger;

    private class InFlight
    {
        public required DiscoveryThrow Throw;
        public required int StartTick;
        public Vec3 Last;
        public bool Seen;
        public int Bounces;
        public int Still;
        public List<Vec3> Path = new List<Vec3>();
    }

    private readonly Dictionary<uint, InFlight> _inFlight = new();
    private readonly Queue<DiscoveryThrow> _releasing = new();

    private DiscoveryJob? _job;
    private DiscoveryPlan? _plan;
    private DateTime _startedAt;
    private Action<List<DiscoveryOutcome>>? _report;
    private Action<string?>? _done;
    private bool _closing;
    private int _tick;
    private int _lastRelease;
    private int _ownerAsks;
    private int _ownerAskedTick;

    public PracticeDiscovery(ISwiftlyCore core, GrenadeEmitter emitter, ILogger<PracticeDiscovery> logger)
    {
        _core = core;
        _emitter = emitter;
        _logger = logger;
    }

    public bool IsBusy => _plan != null;

    public string? JobId => _job?.id;

    public bool Start(DiscoveryJob job, Action<List<DiscoveryOutcome>> report, Action<string?> done)
    {
        if (IsBusy)
        {
            return false;
        }

        _job = job;
        _plan = new DiscoveryPlan(job);
        _startedAt = DateTime.UtcNow;
        _report = report;
        _done = done;
        _closing = false;

        _logger.LogInformation(
            "discovery {id}: {count} proposals for {map}, at most {grenades} grenades",
            job.id,
            job.candidates.Count,
            job.map,
            job.max_grenades
        );

        return true;
    }

    public bool Cancel()
    {
        if (_plan == null)
        {
            return false;
        }

        Finish("cancelled");
        return true;
    }

    // A map change or an unload under a running discovery says so, rather
    // than leaving the panel to notice minutes later.
    public void Abort(string reason)
    {
        if (_plan != null)
        {
            Finish(reason);
            return;
        }

        Reset();
    }

    public void Reset()
    {
        foreach (uint index in _inFlight.Keys.ToList())
        {
            _emitter.Retire(index);
        }

        _inFlight.Clear();
        _releasing.Clear();
        _plan = null;
        _job = null;
        _report = null;
        _done = null;
        _ownerAsks = 0;
    }

    public void OnTick()
    {
        _tick++;

        if (_plan == null)
        {
            return;
        }

        Release();

        var landed = new List<(uint, Vec3?)>();

        foreach ((uint index, InFlight flight) in _inFlight)
        {
            CBaseCSGrenadeProjectile? projectile = _emitter.ProjectileAt(index);

            if (projectile == null || !projectile.IsValid || _tick - flight.StartTick > TimeoutTicks)
            {
                landed.Add((index, flight.Seen ? flight.Last : null));
                continue;
            }

            Vector? origin = projectile.AbsOrigin;

            if (origin != null)
            {
                flight.Last = new Vec3(origin.Value.X, origin.Value.Y, origin.Value.Z);
                flight.Seen = true;

                if ((_tick - flight.StartTick) % PathEveryTicks == 0 && flight.Path.Count < MaxPathPoints)
                {
                    flight.Path.Add(flight.Last);
                }
            }

            flight.Bounces = projectile.Bounces;

            Vector speed = projectile.AbsVelocity;
            flight.Still = new Vec3(speed.X, speed.Y, speed.Z).Length() < RestSpeed ? flight.Still + 1 : 0;

            if (flight.Seen && flight.Still >= RestTicks)
            {
                landed.Add((index, flight.Last));
            }
        }

        foreach ((uint index, Vec3? at) in landed)
        {
            Land(index, at);
        }
    }

    public void OnDetonated(uint entityIndex, Vec3 position)
    {
        if (_inFlight.ContainsKey(entityIndex))
        {
            Land(entityIndex, position);
        }
    }

    // Reports what has finished and decides when the job is over. Off the
    // tick: nothing here is time-critical.
    public void Pump()
    {
        if (_plan == null || _job == null)
        {
            return;
        }

        List<DiscoveryOutcome> finished = _plan.TakeFinished();

        if (finished.Count > 0)
        {
            _report?.Invoke(finished);
        }

        float elapsed = (float)(DateTime.UtcNow - _startedAt).TotalSeconds;

        if (!_closing && _plan.Exhausted(elapsed))
        {
            _closing = true;
            _releasing.Clear();
        }

        if (_inFlight.Count > 0 || _releasing.Count > 0)
        {
            return;
        }

        if (_closing || _plan.Done)
        {
            _plan.Close();
            List<DiscoveryOutcome> rest = _plan.TakeFinished();

            if (rest.Count > 0)
            {
                _report?.Invoke(rest);
            }

            Finish(null);
        }
    }

    private void Release()
    {
        if (_plan == null || _closing || _tick - _lastRelease < EmitEveryTicks)
        {
            return;
        }

        if (_releasing.Count == 0 && _inFlight.Count < MaxInFlight)
        {
            foreach (DiscoveryThrow next in _plan.Next(1))
            {
                _releasing.Enqueue(next);
            }
        }

        if (_releasing.Count == 0)
        {
            return;
        }

        IPlayer? owner = _emitter.Owner(0, allowBots: true);

        if (owner == null)
        {
            AddOwner();
            return;
        }

        _lastRelease = _tick;
        DiscoveryThrow throwing = _releasing.Dequeue();
        uint? index = _emitter.Emit(_job!.utility_type, throwing.seed, owner);

        if (index == null)
        {
            _plan.Observe(throwing, new DiscoveryLanding { landed = false });
            return;
        }

        _inFlight[index.Value] = new InFlight { Throw = throwing, StartTick = _tick };
    }

    // Nobody plays on a discovery server, and an emitted grenade needs a live
    // pawn to own it. One frozen bot does. Asked for again if it never came or
    // has gone: a map load's practice cfg kicks every bot.
    private void AddOwner()
    {
        if (_ownerAsks > 0 && _tick - _ownerAskedTick < OwnerAskEveryTicks)
        {
            return;
        }

        if (_ownerAsks >= OwnerAsks)
        {
            Finish("no bot could join to throw the grenades");
            return;
        }

        _ownerAsks++;
        _ownerAskedTick = _tick;
        _core.Engine.ExecuteCommand(
            "bot_quota_mode normal;bot_join_after_player 0;bot_stop 1;bot_freeze 1;bot_chatter off;bot_quota 1;bot_add_ct"
        );
        _logger.LogInformation("discovery: no live player to own grenades, adding a bot");
    }

    private void Land(uint index, Vec3? at)
    {
        if (!_inFlight.Remove(index, out InFlight? flight))
        {
            return;
        }

        _emitter.Retire(index);

        if (at != null)
        {
            flight.Path.Add(at.Value);
        }

        _plan?.Observe(
            flight.Throw,
            new DiscoveryLanding
            {
                landed = at != null,
                position = at ?? new Vec3(0f, 0f, 0f),
                bounces = flight.Bounces,
                ticks = _tick - flight.StartTick,
                path = flight.Path,
            }
        );
    }

    private void Finish(string? error)
    {
        Action<string?>? done = _done;
        string id = _job?.id ?? "";
        int grenades = _plan?.Grenades ?? 0;

        Reset();

        _logger.LogInformation(
            "discovery {id}: finished after {grenades} grenades{error}",
            id,
            grenades,
            error == null ? "" : $" ({error})"
        );
        done?.Invoke(error);
    }
}
