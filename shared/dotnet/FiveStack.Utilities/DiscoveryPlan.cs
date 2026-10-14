using FiveStack.Entities.Practice;
using FiveStack.Enums;

namespace FiveStack.Utilities;

public enum eDiscoveryThrow
{
    // The proposal exactly as the flight model put it.
    Proposal,
    // A step of the search around a proposal that missed.
    Refine,
    // A quarter of a degree off a hit, to measure how forgiving it is.
    Probe,
}

// One grenade the plan wants thrown.
public class DiscoveryThrow
{
    public required int track { get; init; }
    public required eDiscoveryThrow kind { get; init; }
    public required float pitch { get; init; }
    public required float yaw { get; init; }
    public required LaunchSeed seed { get; init; }
}

// Where a thrown grenade ended up, as the engine reported it.
public class DiscoveryLanding
{
    public bool landed { get; init; }
    public Vec3 position { get; init; }
    public int bounces { get; init; }
    public int ticks { get; init; }
    public List<Vec3> path { get; init; } = new List<Vec3>();
}

// Verifies a discovery's proposals by throwing them, with no engine types in
// it so the decisions are testable without a server.
//
// A proposal comes from a flight model that is exact through the air and off
// hard surfaces but can be hundreds of units out after a soft bounce. So a
// proposal that lands is probed for forgiveness and kept; one that misses by a
// little is searched around, pitch and yaw at shrinking steps, until it lands
// or runs out of grenades; one that misses by a lot is dropped.
public class DiscoveryPlan
{
    public const float AimJitter = 0.25f;
    public const int MaxRefineGrenades = 24;
    private static readonly float[] RefineSteps = { 0.6f, 0.2f, 0.07f };

    private enum eState
    {
        Proposal,
        Refining,
        Probing,
        Done,
    }

    private class Best
    {
        public float pitch;
        public float yaw;
        public LaunchSeed seed;
        public DiscoveryLanding landing = new DiscoveryLanding();
        public float distance = float.MaxValue;
    }

    private class Track
    {
        public required DiscoveryCandidate candidate;
        public required TechniqueProfile technique;
        public required float strength;
        public eState state = eState.Proposal;
        public Best best = new Best();
        public int step;
        public int outstanding;
        public bool improved;
        public int refineGrenades;
        public int grenades;
        public float forgiveness;
        public DiscoveryOutcome? outcome;
    }

    private readonly DiscoveryJob _job;
    private readonly List<Track> _tracks = new();
    private readonly Queue<DiscoveryThrow> _ready = new();
    private readonly List<DiscoveryOutcome> _finished = new();

    public int Grenades { get; private set; }

    public DiscoveryPlan(DiscoveryJob job)
    {
        _job = job;

        for (int i = 0; i < job.candidates.Count; i++)
        {
            DiscoveryCandidate candidate = job.candidates[i];
            var track = new Track
            {
                candidate = candidate,
                technique = Technique(candidate),
                strength = PracticeLaunchUtility.RawStrength(Strength(candidate.strength)),
            };
            _tracks.Add(track);
            Queue(i, eDiscoveryThrow.Proposal, candidate.pitch, candidate.yaw);
        }
    }

    public static TechniqueProfile Technique(DiscoveryCandidate candidate)
    {
        eMoveDirection direction = candidate.direction switch
        {
            "forward" => eMoveDirection.Forward,
            "back" => eMoveDirection.Back,
            "left" => eMoveDirection.Left,
            "right" => eMoveDirection.Right,
            _ => eMoveDirection.None,
        };
        eMoveGait gait = candidate.gait == "walk" ? eMoveGait.Walk : eMoveGait.Run;

        return TechniqueProfile.Moving(direction, gait, candidate.jump_throw);
    }

    private static eThrowStrength Strength(string strength)
    {
        return strength switch
        {
            "Half" => eThrowStrength.Half,
            "Drop" => eThrowStrength.Drop,
            _ => eThrowStrength.Full,
        };
    }

    public bool Done => _tracks.All(track => track.state == eState.Done);

    // Whether the budget is spent. What is still in the air lands and is
    // counted; nothing new is thrown.
    public bool Exhausted(float elapsedSeconds)
    {
        return Grenades >= _job.max_grenades || elapsedSeconds >= _job.max_seconds;
    }

    // Up to max throws that are ready now.
    public List<DiscoveryThrow> Next(int max)
    {
        var next = new List<DiscoveryThrow>();

        while (next.Count < max && Grenades < _job.max_grenades && _ready.Count > 0)
        {
            DiscoveryThrow throwing = _ready.Dequeue();
            Track track = _tracks[throwing.track];

            track.grenades++;
            if (throwing.kind == eDiscoveryThrow.Refine)
            {
                track.refineGrenades++;
            }
            Grenades++;
            next.Add(throwing);
        }

        return next;
    }

    public void Observe(DiscoveryThrow thrown, DiscoveryLanding landing)
    {
        Track track = _tracks[thrown.track];

        if (track.state == eState.Done)
        {
            return;
        }

        track.outstanding--;
        float distance = landing.landed ? (landing.position - _job.target).Length() : float.MaxValue;

        switch (thrown.kind)
        {
            case eDiscoveryThrow.Proposal:
                Keep(track, thrown, landing, distance);
                AfterProposal(thrown.track, track);
                break;
            case eDiscoveryThrow.Refine:
                if (distance < track.best.distance)
                {
                    Keep(track, thrown, landing, distance);
                    track.improved = true;
                }
                if (track.outstanding == 0)
                {
                    AfterRefineRound(thrown.track, track);
                }
                break;
            case eDiscoveryThrow.Probe:
                track.forgiveness = landing.landed
                    ? MathF.Max(track.forgiveness, (landing.position - track.best.landing.position).Length())
                    : float.MaxValue;
                if (track.outstanding == 0)
                {
                    Finish(track, verified: true);
                }
                break;
        }
    }

    // Everything that has finished since the last call.
    public List<DiscoveryOutcome> TakeFinished()
    {
        var finished = new List<DiscoveryOutcome>(_finished);
        _finished.Clear();
        return finished;
    }

    // The budget ran out: what is still undecided is decided on what was
    // thrown. A hit that was never probed is still a hit.
    public void Close()
    {
        foreach (Track track in _tracks)
        {
            if (track.state == eState.Done)
            {
                continue;
            }

            Finish(track, verified: track.best.distance <= _job.radius);
        }
        _ready.Clear();
    }

    private float NearMiss => MathF.Max(_job.radius * 4f, 256f);

    private void AfterProposal(int index, Track track)
    {
        if (track.best.distance <= _job.radius)
        {
            StartProbing(index, track);
            return;
        }

        if (track.best.distance > NearMiss)
        {
            Finish(track, verified: false);
            return;
        }

        track.state = eState.Refining;
        track.step = 0;
        QueueRefineRound(index, track);
    }

    private void AfterRefineRound(int index, Track track)
    {
        if (track.best.distance <= _job.radius)
        {
            StartProbing(index, track);
            return;
        }

        if (!track.improved)
        {
            track.step++;
        }

        if (track.step >= RefineSteps.Length || track.refineGrenades + 4 > MaxRefineGrenades)
        {
            Finish(track, verified: false);
            return;
        }

        QueueRefineRound(index, track);
    }

    private void QueueRefineRound(int index, Track track)
    {
        float step = RefineSteps[track.step];
        track.improved = false;

        Queue(index, eDiscoveryThrow.Refine, track.best.pitch + step, track.best.yaw);
        Queue(index, eDiscoveryThrow.Refine, track.best.pitch - step, track.best.yaw);
        Queue(index, eDiscoveryThrow.Refine, track.best.pitch, track.best.yaw + step);
        Queue(index, eDiscoveryThrow.Refine, track.best.pitch, track.best.yaw - step);
    }

    private void StartProbing(int index, Track track)
    {
        track.state = eState.Probing;
        track.forgiveness = 0f;

        Queue(index, eDiscoveryThrow.Probe, track.best.pitch + AimJitter, track.best.yaw);
        Queue(index, eDiscoveryThrow.Probe, track.best.pitch - AimJitter, track.best.yaw);
        Queue(index, eDiscoveryThrow.Probe, track.best.pitch, track.best.yaw + AimJitter);
        Queue(index, eDiscoveryThrow.Probe, track.best.pitch, track.best.yaw - AimJitter);
    }

    private void Keep(Track track, DiscoveryThrow thrown, DiscoveryLanding landing, float distance)
    {
        track.best = new Best
        {
            pitch = thrown.pitch,
            yaw = thrown.yaw,
            seed = thrown.seed,
            landing = landing,
            distance = distance,
        };
    }

    private void Queue(int index, eDiscoveryThrow kind, float pitch, float yaw)
    {
        Track track = _tracks[index];
        pitch = Math.Clamp(pitch, -89f, 89f);

        _ready.Enqueue(
            new DiscoveryThrow
            {
                track = index,
                kind = kind,
                pitch = pitch,
                yaw = yaw,
                seed = track.technique.Seed(track.candidate.stand, pitch, yaw, track.strength),
            }
        );
        track.outstanding++;
    }

    private void Finish(Track track, bool verified)
    {
        // A probe still to land when the budget ran out leaves forgiveness
        // unmeasured, which is not the same as perfectly forgiving.
        bool probed = track.state == eState.Probing && track.outstanding == 0;
        track.state = eState.Done;
        bool hit = verified && track.best.distance <= _job.radius;

        var outcome = new DiscoveryOutcome
        {
            result_id = track.candidate.result_id,
            status = hit ? "verified" : "missed",
            pitch = track.best.pitch,
            yaw = track.best.yaw,
            initial_position = track.best.seed.position,
            initial_velocity = track.best.seed.velocity,
            landing = track.best.landing.position,
            distance = track.best.distance == float.MaxValue ? -1f : track.best.distance,
            bounces = track.best.landing.bounces,
            flight_time_ms = track.best.landing.ticks * 1000 / 64,
            forgiveness = hit && probed && track.forgiveness != float.MaxValue ? track.forgiveness : -1f,
            grenades = track.grenades,
            path = hit ? track.best.landing.path : new List<Vec3>(),
        };

        track.outcome = outcome;
        _finished.Add(outcome);
    }
}
