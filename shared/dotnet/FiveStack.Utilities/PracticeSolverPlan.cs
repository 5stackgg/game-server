using FiveStack.Entities.Practice;
using FiveStack.Enums;

namespace FiveStack.Utilities;

// The solve, minus the server.
//
// Hand it a request, ask it for a batch, tell it where those grenades landed,
// ask it for the next batch. It decides when there is nothing left worth
// throwing. Every stop is a named one -- there is no path where this runs out
// of ideas quietly and hands back its best miss as though it were an answer.
public class PracticeSolverPlan
{
    private readonly SolveRequest _request;
    private readonly Queue<SolveCandidate> _pending = new Queue<SolveCandidate>();
    private readonly List<SolveObservation> _observations = new List<SolveObservation>();
    private readonly HashSet<string> _tried = new HashSet<string>();

    private eSolveOutcome _stop = eSolveOutcome.Running;
    private float _step = PracticeSolverUtility.RefineStartStep;
    private float _bestAtPassStart = float.MaxValue;
    private int _refinePass;
    private int _stalledPasses;
    private int _thrown;
    private int _batches;
    private string _phase = "sweep";

    public PracticeSolverPlan(SolveRequest request)
    {
        _request = PracticeSolverUtility.Defaults(request);

        if (_request.strengths.Count == 0)
        {
            _stop = eSolveOutcome.NoCandidates;
            return;
        }

        List<SolveCandidate> sweep = PracticeSolverUtility.CoarseSweep(_request);

        // The sweep is truncated rather than allowed to eat the refinement's
        // budget: a grid fine enough to land on the answer by itself is not
        // what a sweep is for.
        int budget = Math.Max(
            _request.batch_size,
            (int)(_request.max_grenades * PracticeSolverUtility.CoarseShare)
        );

        foreach (SolveCandidate candidate in sweep.Take(budget))
        {
            Enqueue(candidate);
        }

        if (_pending.Count == 0)
        {
            _stop = eSolveOutcome.NoCandidates;
        }
    }

    public SolveRequest Request => _request;

    public IReadOnlyList<SolveObservation> Observations => _observations;

    public int Thrown => _thrown;

    public int Batches => _batches;

    public string Phase => _phase;

    public SolveObservation? Best =>
        _observations
            .Where(observation => observation.landed)
            .OrderBy(observation => observation.distance)
            .FirstOrDefault();

    public bool Converged()
    {
        SolveObservation? best = Best;

        return best != null && best.distance <= _request.tolerance;
    }

    public bool Expired(float elapsedSeconds)
    {
        return elapsedSeconds >= _request.max_seconds;
    }

    // Empty means there is nothing more to throw; Outcome says why.
    public List<SolveCandidate> NextBatch()
    {
        if (_stop != eSolveOutcome.Running)
        {
            return new List<SolveCandidate>();
        }

        // Checked here and not only between phases: a throw inside tolerance
        // during the sweep is the answer, and finishing the grid to confirm it
        // would spend a hundred grenades on a question already settled.
        if (Converged())
        {
            _stop = eSolveOutcome.Converged;
            return new List<SolveCandidate>();
        }

        if (_pending.Count == 0 && !Advance())
        {
            return new List<SolveCandidate>();
        }

        int remaining = _request.max_grenades - _thrown;

        if (remaining <= 0)
        {
            _stop = eSolveOutcome.GrenadeCap;
            return new List<SolveCandidate>();
        }

        int take = Math.Min(Math.Min(_request.batch_size, remaining), _pending.Count);
        var batch = new List<SolveCandidate>(take);

        for (int index = 0; index < take; index++)
        {
            batch.Add(_pending.Dequeue());
        }

        _thrown += batch.Count;
        _batches++;

        return batch;
    }

    public void Observe(SolveObservation observation)
    {
        _observations.Add(observation);
    }

    public SolveResult Finish(float elapsedSeconds)
    {
        SolveObservation? best = Best;
        eSolveOutcome outcome;

        if (best != null && best.distance <= _request.tolerance)
        {
            outcome = eSolveOutcome.Converged;
        }
        else if (Expired(elapsedSeconds))
        {
            outcome = eSolveOutcome.TimedOut;
        }
        else if (_stop != eSolveOutcome.Running)
        {
            outcome = _stop;
        }
        else
        {
            outcome = eSolveOutcome.GrenadeCap;
        }

        return new SolveResult
        {
            outcome = outcome.ToString(),
            message = Describe(outcome, best),
            best = best,
            thrown = _thrown,
            batches = _batches,
            elapsed_seconds = elapsedSeconds,
        };
    }

    public string Progress()
    {
        SolveObservation? best = Best;
        string closest = best == null ? "nothing landed yet" : $"closest {best.distance:0}u";

        return $"{_phase}: {_thrown}/{_request.max_grenades} thrown, {closest}";
    }

    private bool Advance()
    {
        if (Converged())
        {
            _stop = eSolveOutcome.Converged;
            return false;
        }

        if (_refinePass >= PracticeSolverUtility.RefinePasses)
        {
            _stop = eSolveOutcome.NoProgress;
            return false;
        }

        SolveObservation? best = Best;

        if (best == null)
        {
            _stop = eSolveOutcome.NoProgress;
            return false;
        }

        // Only counted once a refinement pass has actually run: the first pass
        // is being compared against the sweep, which is a different question.
        if (_refinePass > 0)
        {
            bool stalled =
                _bestAtPassStart - best.distance < PracticeSolverUtility.MinProgress;

            _stalledPasses = stalled ? _stalledPasses + 1 : 0;

            if (_stalledPasses >= PracticeSolverUtility.MaxStallPasses)
            {
                _stop = eSolveOutcome.NoProgress;
                return false;
            }
        }

        float separation = MathF.Max(
            _step * 2f,
            PracticeSolverUtility.MinSeparationDegrees
        );

        List<SolveObservation> seeds = PracticeSolverUtility.PickDistinct(
            _observations,
            PracticeSolverUtility.RefineSeeds,
            separation
        );

        foreach (SolveObservation seed in seeds)
        {
            foreach (
                SolveCandidate neighbour in PracticeSolverUtility.Neighbours(
                    seed.candidate,
                    _step
                )
            )
            {
                Enqueue(neighbour);
            }
        }

        _bestAtPassStart = best.distance;
        _refinePass++;
        _phase = $"refine {_refinePass}";
        _step /= PracticeSolverUtility.RefineShrink;

        if (_pending.Count == 0)
        {
            _stop = eSolveOutcome.NoProgress;
            return false;
        }

        return true;
    }

    // A refinement pass around neighbouring seeds proposes the same aim twice.
    // Throwing it twice would spend the budget confirming what the engine
    // already told us.
    private void Enqueue(SolveCandidate candidate)
    {
        if (_tried.Add(PracticeSolverUtility.CandidateKey(candidate)))
        {
            _pending.Enqueue(candidate);
        }
    }

    private string Describe(eSolveOutcome outcome, SolveObservation? best)
    {
        switch (outcome)
        {
            case eSolveOutcome.Converged:
                return $"landed {best!.distance:0.0}u from the target after {_thrown} grenades";
            case eSolveOutcome.NoCandidates:
                return _request.strengths.Count == 0
                    ? "no strength has been calibrated, so there is nothing safe to throw"
                    : "the target is too close to the throwing position to be a throw";
            case eSolveOutcome.TimedOut:
                return $"gave up after {_request.max_seconds:0}s; {Closest(best)}";
            case eSolveOutcome.GrenadeCap:
                return $"gave up after {_thrown} grenades; {Closest(best)}";
            default:
                return $"refinement stopped improving; {Closest(best)}";
        }
    }

    private static string Closest(SolveObservation? best)
    {
        return best == null
            ? "no grenade reported a landing"
            : $"the closest throw missed by {best.distance:0.0}u";
    }
}
