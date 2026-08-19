namespace FiveStack.Entities.Practice;

// One throw the solver is about to make: an aim and a release, which is the
// whole of what a human can control.
public struct SolveCandidate
{
    public float pitch { get; set; }
    public float yaw { get; set; }

    // m_flThrowStrength, and the bucket a player would recognise it as. The raw
    // value drives the emit; the bucket names the calibration measurement that
    // licensed it.
    public float strength { get; set; }
    public string strength_bucket { get; set; }
}
