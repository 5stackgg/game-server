using FiveStack.Entities.Practice;

namespace FiveStack.Utilities;

public enum eMoveDirection
{
    None,
    Forward,
    Back,
    Left,
    Right,
}

public enum eMoveGait
{
    Walk,
    Run,
}

// What a player is doing at the instant the grenade leaves their hand, as the
// launch model needs it: the eye's height over the stance and the player's own
// velocity, which the engine adds to the throw. A lineup a person can repeat is
// one whose technique always produces the same pair, so these are measured
// from throws the engine recorded rather than worked out from movement code.
public class TechniqueProfile
{
    // A standing eye, from the recorded standing throws.
    public const float StandingEye = 63.97f;

    // A jump throw bind lets go a moment into the jump. Every recorded jump
    // throw on de_mirage leaves at this lift with the eye this far over the
    // stance, to a tenth of a unit.
    public const float JumpThrowLift = 218.8f;
    public const float JumpThrowEye = 89.91f;

    // Moving at full speed with a grenade out. Recorded running throws were all
    // still accelerating (192-234 u/s), so a full-speed run is not measured yet
    // and neither is a walk; Measured says which profiles are.
    public const float RunSpeed = 245f;
    public const float WalkSpeed = 127.4f;

    public required string technique { get; init; }
    public eMoveDirection direction { get; init; }
    public eMoveGait gait { get; init; }
    public bool jump_throw { get; init; }

    // Horizontal speed along the direction, and the vertical speed.
    public float speed { get; init; }
    public float lift { get; init; }

    // The eye's height over the stance at release.
    public float eye { get; init; }

    public bool measured { get; init; }

    public static readonly TechniqueProfile Standing = new TechniqueProfile
    {
        technique = "Stationary",
        eye = StandingEye,
        measured = true,
    };

    public static readonly TechniqueProfile JumpThrow = new TechniqueProfile
    {
        technique = "Jump",
        jump_throw = true,
        lift = JumpThrowLift,
        eye = JumpThrowEye,
        measured = true,
    };

    public static TechniqueProfile Moving(eMoveDirection direction, eMoveGait gait, bool jumpThrow)
    {
        if (direction == eMoveDirection.None)
        {
            return jumpThrow ? JumpThrow : Standing;
        }

        string technique = (gait, jumpThrow) switch
        {
            (eMoveGait.Walk, false) => "Walking",
            (eMoveGait.Run, false) => "Running",
            (eMoveGait.Walk, true) => "WalkJump",
            _ => "RunJump",
        };

        return new TechniqueProfile
        {
            technique = technique,
            direction = direction,
            gait = gait,
            jump_throw = jumpThrow,
            speed = gait == eMoveGait.Run ? RunSpeed : WalkSpeed,
            lift = jumpThrow ? JumpThrowLift : 0f,
            eye = jumpThrow ? JumpThrowEye : StandingEye,
            measured = false,
        };
    }

    // The player's velocity at release, for someone facing yaw. Left and right
    // are a strafe, so the throw still goes where they are looking.
    public Vec3 Velocity(float yaw)
    {
        float radians = yaw * MathF.PI / 180f;
        var forward = new Vec3(MathF.Cos(radians), MathF.Sin(radians), 0f);
        var right = new Vec3(MathF.Sin(radians), -MathF.Cos(radians), 0f);

        Vec3 along = direction switch
        {
            eMoveDirection.Forward => forward,
            eMoveDirection.Back => forward * -1f,
            eMoveDirection.Right => right,
            eMoveDirection.Left => right * -1f,
            _ => new Vec3(0f, 0f, 0f),
        };

        return (along * speed) + new Vec3(0f, 0f, lift);
    }

    public Vec3 Eye(Vec3 stance)
    {
        return stance + new Vec3(0f, 0f, eye);
    }

    public LaunchSeed Seed(Vec3 stance, float pitch, float yaw, float strength)
    {
        return PracticeLaunchUtility.Seed(Eye(stance), pitch, yaw, strength, Velocity(yaw));
    }
}
