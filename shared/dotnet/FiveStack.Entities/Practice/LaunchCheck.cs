namespace FiveStack.Entities.Practice;

// One recorded throw held up against the launch model, so a failed calibration
// says which throw disagreed and by how much rather than just "no".
public class LaunchCheck
{
    public string client_id { get; set; } = "";
    public string strength { get; set; } = "";
    public float pitch { get; set; }

    public float position_error { get; set; }
    public float direction_error { get; set; }

    // Observed release speed over predicted. One is a perfect model; the solver
    // carries whatever this is forward as a correction rather than insisting on
    // one, because a systematic few percent is a constant being slightly off
    // and not the formula being wrong.
    public float speed_ratio { get; set; }

    public bool passed { get; set; }
}
