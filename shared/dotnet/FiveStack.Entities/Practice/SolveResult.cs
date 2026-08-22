using FiveStack.Enums;

namespace FiveStack.Entities.Practice;

// How a solve ended, and the best real throw it saw.
public class SolveResult
{
    public string outcome { get; set; } = nameof(eSolveOutcome.Running);
    public string message { get; set; } = "";

    public SolveObservation? best { get; set; }

    public int thrown { get; set; }
    public int batches { get; set; }
    public float elapsed_seconds { get; set; }

    public bool Converged()
    {
        return outcome == nameof(eSolveOutcome.Converged);
    }
}
