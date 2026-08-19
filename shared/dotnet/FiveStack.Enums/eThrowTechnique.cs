namespace FiveStack.Enums;

// Mirrors public.e_nade_techniques. Movement and stance are one value rather
// than two flags because that is how a player thinks about reproducing a
// lineup: "running jump throw" is a single instruction.
public enum eThrowTechnique
{
    Stationary,
    Walking,
    Running,
    Crouch,
    Jump,
    RunJump,
    WalkJump,
    CrouchJump,
}
