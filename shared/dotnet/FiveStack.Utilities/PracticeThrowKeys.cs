using FiveStack.Entities.Practice;
using FiveStack.Enums;

namespace FiveStack.Utilities;

// A throw as the things a hand does: which mouse buttons, which keys.
public record ThrowKeys(
    string Word,
    string Figure,
    bool LeftClick,
    bool RightClick,
    bool Jump,
    bool Crouch,
    bool Walk,
    IReadOnlyList<string> Move,
    string RunUp
);

public static class PracticeThrowKeys
{
    public static readonly IReadOnlyList<string> MoveKeys = new[] { "W", "A", "S", "D" };

    public static ThrowKeys For(LineupRecord lineup)
    {
        RunUp? runUp = PracticeMarkerUtility.RunUpOf(lineup.approach);
        string technique = lineup.technique;

        bool jump =
            lineup.release.jump_throw
            || runUp?.Jump == true
            || technique
                is nameof(eThrowTechnique.Jump)
                    or nameof(eThrowTechnique.RunJump)
                    or nameof(eThrowTechnique.WalkJump)
                    or nameof(eThrowTechnique.CrouchJump);

        bool crouch =
            runUp?.Crouch == true
            || technique is nameof(eThrowTechnique.Crouch) or nameof(eThrowTechnique.CrouchJump);

        bool walk =
            runUp?.Walk == true
            || technique is nameof(eThrowTechnique.Walking) or nameof(eThrowTechnique.WalkJump);

        // A moving throw with no recorded run-up still moves: forward is the
        // only direction the technique alone can promise.
        bool moving =
            technique
                is nameof(eThrowTechnique.Walking)
                    or nameof(eThrowTechnique.Running)
                    or nameof(eThrowTechnique.RunJump)
                    or nameof(eThrowTechnique.WalkJump);

        IReadOnlyList<string> move =
            runUp?.Keys ?? (moving ? new[] { "W" } : Array.Empty<string>());

        return new ThrowKeys(
            Word(technique, jump),
            Figure(technique, jump),
            lineup.strength != nameof(eThrowStrength.Drop),
            lineup.strength is nameof(eThrowStrength.Half) or nameof(eThrowStrength.Drop),
            jump,
            crouch,
            walk,
            move,
            runUp == null ? "" : PracticeMarkerUtility.RunUpLine(runUp)
        );
    }

    // One per eThrowTechnique, lower case: the name of the drawing on the
    // throw bar. A throw on the jump bind is drawn jumping even when it was
    // recorded before the technique said so.
    public static readonly IReadOnlyList<string> Figures = new[]
    {
        "stationary",
        "walking",
        "running",
        "crouch",
        "jump",
        "runjump",
        "walkjump",
        "crouchjump",
    };

    private static string Figure(string technique, bool jump)
    {
        return technique switch
        {
            nameof(eThrowTechnique.Walking) => jump ? "walkjump" : "walking",
            nameof(eThrowTechnique.Running) => jump ? "runjump" : "running",
            nameof(eThrowTechnique.Crouch) => jump ? "crouchjump" : "crouch",
            nameof(eThrowTechnique.Jump) => "jump",
            nameof(eThrowTechnique.RunJump) => "runjump",
            nameof(eThrowTechnique.WalkJump) => "walkjump",
            nameof(eThrowTechnique.CrouchJump) => "crouchjump",
            _ => jump ? "jump" : "stationary",
        };
    }

    private static string Word(string technique, bool jump)
    {
        return technique switch
        {
            nameof(eThrowTechnique.Walking) => "Walk throw",
            nameof(eThrowTechnique.Running) => "Run throw",
            nameof(eThrowTechnique.Crouch) => "Crouch throw",
            nameof(eThrowTechnique.Jump) => "Jump throw",
            nameof(eThrowTechnique.RunJump) => "Run + jump throw",
            nameof(eThrowTechnique.WalkJump) => "Walk + jump throw",
            nameof(eThrowTechnique.CrouchJump) => "Crouch + jump throw",
            _ => jump ? "Jump throw" : "Stand still",
        };
    }
}
