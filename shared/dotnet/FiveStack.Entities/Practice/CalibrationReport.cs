using FiveStack.Enums;

namespace FiveStack.Entities.Practice;

// The solver's licence to run on one map, and the evidence behind it.
//
// Held per map per server boot: a map change replaces the collision mesh the
// whole exercise is about, and a plugin reload is the only thing that could
// have changed the launch model.
public class CalibrationReport
{
    public string map { get; set; } = "";
    public string status { get; set; } = nameof(eCalibrationStatus.Unknown);
    public string message { get; set; } = "";

    public List<LaunchCheck> launch_checks { get; set; } = new List<LaunchCheck>();

    // Measured release speed over predicted, per strength bucket. Only buckets
    // somebody actually threw appear here, and the solver searches only these:
    // an unmeasured bucket is a guess about the strength curve, and a guess is
    // exactly what this whole file exists to refuse.
    public Dictionary<string, float> speed_corrections { get; set; } =
        new Dictionary<string, float>();

    // Distance between where the re-emitted grenade landed and where the
    // original one did. Negative until the replay has run.
    public float seed_replay_error { get; set; } = -1f;
    public string seed_replay_client_id { get; set; } = "";
    public string seed_replay_utility { get; set; } = "";

    public bool CanSolve()
    {
        return status == nameof(eCalibrationStatus.Ready) && speed_corrections.Count > 0;
    }

    public List<string> SolvableStrengths()
    {
        return speed_corrections.Keys.OrderBy(name => name, StringComparer.Ordinal).ToList();
    }

    public float CorrectionFor(string strength)
    {
        return speed_corrections.TryGetValue(strength, out float correction) ? correction : 1f;
    }

    public float WorstPositionError()
    {
        return launch_checks.Count == 0 ? 0f : launch_checks.Max(check => check.position_error);
    }

    public float WorstDirectionError()
    {
        return launch_checks.Count == 0 ? 0f : launch_checks.Max(check => check.direction_error);
    }
}
