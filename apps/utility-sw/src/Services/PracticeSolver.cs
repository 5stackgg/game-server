using FiveStack.Entities.Practice;
using FiveStack.Enums;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace UtilityPractice;

// Finds a throw that lands on a chosen point by throwing.
//
// There is no physics in here. Reimplementing CS2's grenade behaviour against a
// collision mesh is weeks of work that drifts every time the game updates, and
// it is unnecessary: a practice server already is the physics engine. The
// solver emits real grenades from real seeds and reads where they really went,
// so the only thing it can be wrong about is which throws it chose to try.
//
// Everything that decides anything lives in PracticeSolverPlan and
// PracticeCalibrationUtility, which have no engine types in them and are tested
// without a server. This class is the part that cannot be: emitting, hiding,
// sampling, reaping.
public class PracticeSolver
{
    // A grenade that has not reported in ten seconds is stuck in geometry or
    // was eaten by something. Its slot is worth more than its answer.
    private const int ProjectileTimeoutTicks = 64 * 10;

    // Grenades collide with each other. Twenty released from one eye position
    // on one tick would spend the batch bouncing off their own siblings, and
    // every landing point in it would be a measurement of that rather than of
    // the map. Spacing the releases puts about twenty units between consecutive
    // grenades on similar lines, which is more than a grenade is wide.
    private const int EmitEveryTicks = 2;

    private readonly ISwiftlyCore _core;
    private readonly PracticeRecorder _recorder;
    private readonly ILogger<PracticeSolver> _logger;

    private enum FlightKind
    {
        Solve,
        Calibration,
        Confirmation,
    }

    private class InFlight
    {
        public required SolveCandidate Candidate;
        public required int StartTick;
        public required FlightKind Kind;
        public Vec3 Last;
        public bool Seen;
        public int Bounces;
    }

    private readonly Dictionary<uint, InFlight> _inFlight = new();
    private readonly Queue<SolveCandidate> _releasing = new();
    private readonly Dictionary<string, CalibrationReport> _calibrated = new();

    private PracticeSolverPlan? _plan;
    private CalibrationReport _calibration = new CalibrationReport();
    private DateTime _startedAt;
    private ulong _owner;
    private Action<string>? _progress;
    private Action<SolveResult, LineupRecord?>? _finished;

    private bool _confirming;
    private SolveObservation? _confirmation;

    private CalibrationReport? _calibrating;
    private LineupRecord? _replaySample;
    private Action<CalibrationReport>? _calibrationDone;

    private int _tick;
    private int _lastRelease;

    public PracticeSolver(
        ISwiftlyCore core,
        PracticeRecorder recorder,
        ILogger<PracticeSolver> logger
    )
    {
        _core = core;
        _recorder = recorder;
        _logger = logger;
    }

    public bool IsBusy => _plan != null || _calibrating != null;

    public string BusyWith => _plan != null ? "a solve" : "a calibration";

    // EventMolotovDetonate carries no entity index, so the recorder matches a
    // molotov to a throw by its thrower. A solver molotov is owned by a real
    // player's pawn, which means the recorder would read it as that player's
    // own throw landing and finalize their lineup at the solver's landing
    // point. While solver molotovs are in the air, nobody's molotov is
    // attributable.
    public bool EmittingMolotovs =>
        (_plan != null && _plan.Request.utility_type == "Molotov")
        || (_calibrating != null && _replaySample?.utility_type == "Molotov");

    public CalibrationReport? CalibrationFor(string map)
    {
        return _calibrated.TryGetValue(map, out CalibrationReport? report) ? report : null;
    }

    public void Forget(string map)
    {
        _calibrated.Remove(map);
    }

    public void Reset()
    {
        foreach (uint index in _inFlight.Keys.ToList())
        {
            Retire(index);
        }

        _inFlight.Clear();
        _releasing.Clear();
        _calibrated.Clear();
        _plan = null;
        _confirming = false;
        _confirmation = null;
        _calibrating = null;
        _replaySample = null;
        _progress = null;
        _finished = null;
        _calibrationDone = null;
    }

    // Re-emits a throw whose landing point is already known and answers whether
    // the engine put it back in the same place.
    //
    // This is the precondition for everything else. A solve that ran without it
    // would emit hundreds of grenades against an assumption nobody had ever
    // tested, and hand back lineups that are confidently wrong -- which a player
    // discovers only by walking to the spot and throwing.
    public bool Calibrate(
        string map,
        IEnumerable<LineupRecord> samples,
        Action<CalibrationReport> done
    )
    {
        if (IsBusy)
        {
            return false;
        }

        var pool = samples.ToList();
        CalibrationReport report = PracticeCalibrationUtility.CheckLaunchModel(map, pool);

        if (!PracticeCalibrationUtility.LaunchModelPassed(report))
        {
            return Answer(report, done);
        }

        LineupRecord? sample = PracticeCalibrationUtility.PickReplaySample(pool);
        IPlayer? owner = Owner();

        if (sample == null)
        {
            report.status = nameof(eCalibrationStatus.NoSample);
            report.message = "no throw to replay";
            return Answer(report, done);
        }

        if (owner == null)
        {
            report.status = nameof(eCalibrationStatus.NoSample);
            report.message =
                "a seeded replay needs a live player to own the projectile, and nobody is on the server";
            return Answer(report, done);
        }

        uint? index = Emit(
            sample.utility_type,
            new LaunchSeed
            {
                position = sample.initial_position,
                velocity = sample.initial_velocity,
            },
            owner
        );

        if (index == null)
        {
            report.status = nameof(eCalibrationStatus.Unsupported);
            report.message = "the server refused to emit a grenade";
            return Answer(report, done);
        }

        _calibrating = report;
        _replaySample = sample;
        _calibrationDone = done;

        _inFlight[index.Value] = new InFlight
        {
            Candidate = default,
            StartTick = _tick,
            Kind = FlightKind.Calibration,
        };

        return true;
    }

    public bool Start(
        SolveRequest request,
        CalibrationReport calibration,
        Action<string> progress,
        Action<SolveResult, LineupRecord?> finished
    )
    {
        if (IsBusy)
        {
            return false;
        }

        request.strengths = calibration.SolvableStrengths();

        _owner = ulong.TryParse(request.requested_by, out ulong steamId) ? steamId : 0;
        _plan = new PracticeSolverPlan(request);
        _calibration = calibration;
        _startedAt = DateTime.UtcNow;
        _progress = progress;
        _finished = finished;
        _confirming = false;
        _confirmation = null;

        return true;
    }

    public bool Cancel()
    {
        if (_plan == null)
        {
            return false;
        }

        Complete(Elapsed(), cancelled: true);
        return true;
    }

    // Sampling has to happen on the game tick: a projectile that vanishes
    // between two slower polls takes its landing point with it. Releasing does
    // too, because the spacing between grenades is measured in ticks.
    public void OnTick()
    {
        _tick++;

        Release();

        if (_inFlight.Count == 0)
        {
            return;
        }

        var expired = new List<uint>();

        foreach ((uint index, InFlight flight) in _inFlight)
        {
            CBaseCSGrenadeProjectile? projectile = TryProjectileAt(index);

            if (projectile == null || !projectile.IsValid)
            {
                expired.Add(index);
                continue;
            }

            if (_tick - flight.StartTick > ProjectileTimeoutTicks)
            {
                expired.Add(index);
                continue;
            }

            Vector? origin = projectile.AbsOrigin;

            if (origin != null)
            {
                flight.Last = new Vec3(origin.Value.X, origin.Value.Y, origin.Value.Z);
                flight.Seen = true;
            }

            flight.Bounces = projectile.Bounces;
        }

        foreach (uint index in expired)
        {
            Land(index, null);
        }
    }

    // Called from the same detonate handlers the recorder uses. Only ever
    // matches a projectile this class emitted; a player's throw is not in the
    // dictionary and falls straight through.
    public void OnDetonated(uint entityIndex, Vec3 position)
    {
        if (_inFlight.ContainsKey(entityIndex))
        {
            Land(entityIndex, position);
        }
    }

    // Drives the batches. Nothing new is queued while anything is still in the
    // air or waiting to be released: a batch is the unit of both the entity
    // budget and the reporting.
    public void Pump()
    {
        if (_plan == null || _inFlight.Count > 0 || _releasing.Count > 0)
        {
            return;
        }

        if (_confirming)
        {
            Confirmed();
            return;
        }

        float elapsed = Elapsed();

        if (_plan.Expired(elapsed))
        {
            Complete(elapsed, cancelled: false);
            return;
        }

        if (_plan.Batches > 0)
        {
            _progress?.Invoke(_plan.Progress());
        }

        List<SolveCandidate> batch = _plan.NextBatch();

        if (batch.Count == 0)
        {
            Complete(elapsed, cancelled: false);
            return;
        }

        foreach (SolveCandidate candidate in batch)
        {
            _releasing.Enqueue(candidate);
        }
    }

    // Entity indices are reused, so a standing block outlives its entity unless
    // it is lifted. Solver projectiles come and go by the hundred, which makes
    // this the one place a leaked block would be guaranteed rather than
    // unlikely.
    public void RefreshVisibility()
    {
        foreach (uint index in _inFlight.Keys)
        {
            Hide(index);
        }
    }

    private void Release()
    {
        if (_releasing.Count == 0 || _plan == null)
        {
            return;
        }

        if (_tick - _lastRelease < EmitEveryTicks)
        {
            return;
        }

        _lastRelease = _tick;

        SolveCandidate candidate = _releasing.Dequeue();
        IPlayer? owner = Owner();

        if (owner == null)
        {
            _releasing.Clear();
            _progress?.Invoke("nobody is on the server to own the projectiles");
            Complete(Elapsed(), cancelled: true);
            return;
        }

        LaunchSeed seed = PracticeSolverUtility.SeedFor(_plan.Request, candidate, _calibration);
        uint? index = Emit(_plan.Request.utility_type, seed, owner);

        if (index == null)
        {
            _plan.Observe(new SolveObservation { candidate = candidate });
            return;
        }

        _inFlight[index.Value] = new InFlight
        {
            Candidate = candidate,
            StartTick = _tick,
            Kind = FlightKind.Solve,
        };
    }

    private bool Answer(CalibrationReport report, Action<CalibrationReport> done)
    {
        _calibrated[report.map] = report;
        done(report);
        return true;
    }

    private float Elapsed()
    {
        return (float)(DateTime.UtcNow - _startedAt).TotalSeconds;
    }

    private void Land(uint index, Vec3? detonation)
    {
        if (!_inFlight.Remove(index, out InFlight? flight))
        {
            return;
        }

        Retire(index);

        Vec3? landing = detonation ?? (flight.Seen ? flight.Last : null);

        if (flight.Kind == FlightKind.Calibration)
        {
            FinishCalibration(landing);
            return;
        }

        if (_plan == null)
        {
            return;
        }

        var observation = new SolveObservation
        {
            candidate = flight.Candidate,
            landing = landing ?? new Vec3(0f, 0f, 0f),
            distance =
                landing == null
                    ? float.MaxValue
                    : (landing.Value - _plan.Request.target).Length(),
            landed = landing != null,
            bounces = flight.Bounces,
        };

        if (flight.Kind == FlightKind.Confirmation)
        {
            _confirmation = observation;
            return;
        }

        _plan.Observe(observation);
    }

    private void FinishCalibration(Vec3? landing)
    {
        CalibrationReport? report = _calibrating;
        LineupRecord? sample = _replaySample;
        Action<CalibrationReport>? done = _calibrationDone;

        _calibrating = null;
        _replaySample = null;
        _calibrationDone = null;

        if (report == null || sample == null)
        {
            return;
        }

        PracticeCalibrationUtility.WithSeedReplay(report, sample, landing);
        _calibrated[report.map] = report;

        done?.Invoke(report);
    }

    // The winning throw, thrown once more on its own.
    //
    // A search grenade shares the air with nineteen others, and grenades bounce
    // off each other. A candidate that was deflected onto the target by a
    // sibling looks like the answer and is not, and nothing later in the
    // pipeline could tell the difference -- the lineup would simply not
    // reproduce for whoever saved it. One grenade with an empty sky is what
    // separates a measurement from a coincidence.
    private bool Confirm(SolveObservation best)
    {
        if (_plan == null)
        {
            return false;
        }

        IPlayer? owner = Owner();

        if (owner == null)
        {
            return false;
        }

        LaunchSeed seed = PracticeSolverUtility.SeedFor(
            _plan.Request,
            best.candidate,
            _calibration
        );

        uint? index = Emit(_plan.Request.utility_type, seed, owner);

        if (index == null)
        {
            return false;
        }

        _confirming = true;
        _confirmation = null;

        _inFlight[index.Value] = new InFlight
        {
            Candidate = best.candidate,
            StartTick = _tick,
            Kind = FlightKind.Confirmation,
        };

        _progress?.Invoke("confirming the winning throw on its own...");

        return true;
    }

    private void Confirmed()
    {
        // A confirmation grenade that never reported is still an answer: it
        // is a throw that did not reproduce.
        SolveObservation confirmation = _confirmation ?? new SolveObservation();

        _confirming = false;
        _confirmation = null;

        Complete(Elapsed(), cancelled: false, confirmation: confirmation);
    }

    private void Complete(float elapsed, bool cancelled, SolveObservation? confirmation = null)
    {
        PracticeSolverPlan? plan = _plan;

        if (plan == null)
        {
            return;
        }

        SolveResult result = plan.Finish(elapsed);

        // Converged on paper. Throw it once more alone before saying so.
        if (
            confirmation == null
            && !cancelled
            && !_confirming
            && result.Converged()
            && result.best != null
            && Confirm(result.best)
        )
        {
            return;
        }

        Action<SolveResult, LineupRecord?>? finished = _finished;
        CalibrationReport calibration = _calibration;

        _plan = null;
        _progress = null;
        _finished = null;
        _confirming = false;
        _confirmation = null;
        _releasing.Clear();

        foreach (uint index in _inFlight.Keys.ToList())
        {
            Retire(index);
        }

        _inFlight.Clear();

        LineupRecord? lineup = null;

        if (result.Converged() && result.best != null)
        {
            if (
                confirmation != null
                && PracticeSolverUtility.Confirms(confirmation, plan.Request)
            )
            {
                // The confirmation is the throw that gets saved: it is the one
                // nothing else was in the air for.
                result.best = confirmation;
                result.message =
                    $"{result.message}, confirmed at {confirmation.distance:0.0}u";

                lineup = PracticeSolverUtility.ToLineup(
                    plan.Request,
                    confirmation,
                    calibration,
                    "swiftlys2",
                    ""
                );
            }
            else if (confirmation != null)
            {
                result.outcome = nameof(eSolveOutcome.NoProgress);
                result.message = confirmation.landed
                    ? $"the winning throw landed {confirmation.distance:0.0}u away on its own, so what the search found was solver grenades colliding rather than a lineup"
                    : "the winning throw did not report a landing when thrown on its own";
            }
            else
            {
                result.outcome = nameof(eSolveOutcome.Refused);
                result.message = "stopped before the winning throw could be confirmed";
            }
        }
        else if (cancelled)
        {
            result.outcome = nameof(eSolveOutcome.Refused);
            result.message = "cancelled";
        }

        finished?.Invoke(result, lineup);
    }

    private IPlayer? Owner()
    {
        IPlayer? preferred = null;

        foreach (IPlayer player in _core.PlayerManager.GetAllPlayers())
        {
            if (player == null || !player.IsValid || player.IsFakeClient || !player.IsAlive)
            {
                continue;
            }

            CBasePlayerPawn? pawn = player.Pawn;

            if (pawn == null || !pawn.IsValid)
            {
                continue;
            }

            if (player.SteamID == _owner)
            {
                return player;
            }

            preferred ??= player;
        }

        return preferred;
    }

    private uint? Emit(string utilityType, LaunchSeed seed, IPlayer owner)
    {
        CBasePlayerPawn? pawn = owner.Pawn;

        if (pawn == null || !pawn.IsValid)
        {
            return null;
        }

        var position = new Vector(seed.position.x, seed.position.y, seed.position.z);
        var velocity = new Vector(seed.velocity.x, seed.velocity.y, seed.velocity.z);

        (float pitch, float yaw) = TrajectoryUtility.AnglesFromVelocity(seed.velocity);
        var angles = new QAngle(pitch, yaw, 0);

        Team team = owner.Controller.Team;

        // The recorder's own guard against counting a grenade the plugin threw
        // as a lineup somebody made or an attempt somebody took. A solve emits
        // hundreds, so this is the difference between a solve and a library
        // full of rubbish.
        _recorder.Emitting = true;

        try
        {
            CBaseCSGrenadeProjectile projectile;

            switch (utilityType)
            {
                case "Smoke":
                    projectile = _core.Game.EmitSmokeGrenade(
                        position,
                        angles,
                        velocity,
                        team,
                        pawn
                    );
                    break;
                case "Flash":
                    projectile = _core.Game.EmitFlashbang(position, angles, velocity, pawn);
                    break;
                case "HighExplosive":
                    projectile = _core.Game.EmitHEGrenade(position, angles, velocity, pawn);
                    break;
                case "Molotov":
                    projectile = _core.Game.EmitMolotov(position, angles, velocity, team, pawn);
                    break;
                default:
                    projectile = _core.Game.EmitDecoy(position, angles, velocity, pawn);
                    break;
            }

            if (!projectile.IsValid)
            {
                return null;
            }

            _recorder.Forget(projectile.Index);
            Hide(projectile.Index);

            return projectile.Index;
        }
        catch (Exception error)
        {
            _logger.LogError(error, "unable to emit a solver {utility}", utilityType);
            return null;
        }
        finally
        {
            _recorder.Emitting = false;
        }
    }

    // Deferred a tick for two reasons: this is usually called from inside the
    // projectile's own detonate event, and the transmit block has to outlive
    // the entity or it shows for the frame between the two.
    private void Retire(uint index)
    {
        _core.Scheduler.NextTick(() =>
        {
            CBaseCSGrenadeProjectile? projectile = TryProjectileAt(index);

            if (projectile != null && projectile.IsValid)
            {
                projectile.Despawn();
            }

            Unhide(index);
        });
    }

    private void Hide(uint index)
    {
        Block(index, true);
    }

    private void Unhide(uint index)
    {
        Block(index, false);
    }

    // Off for the same reason PracticeReplay.TransmitBlocking is: SwiftlyS2
    // 1.4.4 crashes in CheckTransmit. A solver probe being visible for the
    // moment it exists is cosmetic; the server going down is not.
    private void Block(uint index, bool hidden)
    {
        if (!PracticeReplay.TransmitBlocking)
        {
            return;
        }

        foreach (IPlayer viewer in _core.PlayerManager.GetAllPlayers())
        {
            if (viewer != null && viewer.IsValid)
            {
                viewer.ShouldBlockTransmitEntity((int)index, hidden);
            }
        }
    }

    private CBaseCSGrenadeProjectile? TryProjectileAt(uint index)
    {
        try
        {
            return _core.EntitySystem.GetEntityByIndex<CBaseCSGrenadeProjectile>(index);
        }
        catch
        {
            return null;
        }
    }
}
