using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using Xunit;

public class TechniqueProfileTests
{
    private static readonly Vec3 Stance = new Vec3(100f, 200f, -168f);

    [Fact]
    public void StandingStillAddsNothingToTheThrow()
    {
        Vec3 velocity = TechniqueProfile.Standing.Velocity(45f);

        Assert.Equal(0f, velocity.Length(), 4);
        Assert.Equal(-168f + TechniqueProfile.StandingEye, TechniqueProfile.Standing.Eye(Stance).z, 3);
    }

    [Fact]
    public void AJumpThrowRisesStraightUp()
    {
        Vec3 velocity = TechniqueProfile.JumpThrow.Velocity(-161f);

        Assert.Equal(0f, velocity.x, 4);
        Assert.Equal(0f, velocity.y, 4);
        Assert.Equal(TechniqueProfile.JumpThrowLift, velocity.z, 3);
        Assert.Equal("Jump", TechniqueProfile.JumpThrow.technique);
        Assert.True(TechniqueProfile.JumpThrow.measured);
    }

    // Left and right are strafes: the player still faces where they throw.
    [Theory]
    [InlineData(eMoveDirection.Forward, 0f, 1f, 0f)]
    [InlineData(eMoveDirection.Back, 0f, -1f, 0f)]
    [InlineData(eMoveDirection.Right, 0f, 0f, -1f)]
    [InlineData(eMoveDirection.Left, 0f, 0f, 1f)]
    [InlineData(eMoveDirection.Forward, 90f, 0f, 1f)]
    [InlineData(eMoveDirection.Right, 90f, 1f, 0f)]
    public void MovesTheWayThePlayerPressed(eMoveDirection direction, float yaw, float x, float y)
    {
        TechniqueProfile profile = TechniqueProfile.Moving(direction, eMoveGait.Run, false);
        Vec3 velocity = profile.Velocity(yaw);

        Assert.Equal(x * TechniqueProfile.RunSpeed, velocity.x, 2);
        Assert.Equal(y * TechniqueProfile.RunSpeed, velocity.y, 2);
        Assert.Equal(0f, velocity.z, 4);
        Assert.Equal("Running", profile.technique);
    }

    [Fact]
    public void NamesEachMoveAfterTheLineupTechniqueItSavesAs()
    {
        Assert.Equal("Walking", TechniqueProfile.Moving(eMoveDirection.Left, eMoveGait.Walk, false).technique);
        Assert.Equal("WalkJump", TechniqueProfile.Moving(eMoveDirection.Forward, eMoveGait.Walk, true).technique);
        Assert.Equal("RunJump", TechniqueProfile.Moving(eMoveDirection.Back, eMoveGait.Run, true).technique);
        Assert.Same(TechniqueProfile.JumpThrow, TechniqueProfile.Moving(eMoveDirection.None, eMoveGait.Run, true));
        Assert.Same(TechniqueProfile.Standing, TechniqueProfile.Moving(eMoveDirection.None, eMoveGait.Walk, false));
    }

    // Until a full-speed run and walk have been recorded, a discovery has to
    // say its moving throws rest on an assumed speed.
    [Fact]
    public void SaysWhichProfilesAreMeasured()
    {
        Assert.True(TechniqueProfile.Standing.measured);
        Assert.False(TechniqueProfile.Moving(eMoveDirection.Forward, eMoveGait.Run, false).measured);
    }

    [Fact]
    public void ARunningJumpThrowCarriesBothMoves()
    {
        Vec3 velocity = TechniqueProfile.Moving(eMoveDirection.Forward, eMoveGait.Run, true).Velocity(0f);

        Assert.Equal(TechniqueProfile.RunSpeed, velocity.x, 2);
        Assert.Equal(TechniqueProfile.JumpThrowLift, velocity.z, 2);
    }

    // The seed a profile throws is the launch model fed with its eye and its
    // velocity, and for a jump throw that is what the engine recorded.
    [Fact]
    public void ThrowsTheSeedARecordedJumpThrowHad()
    {
        LaunchSeed seed = TechniqueProfile.JumpThrow.Seed(
            new Vec3(908.68005f, -1036.0312f, -251.96875f),
            -55.324947f,
            -161.02596f,
            1f
        );

        Assert.InRange((seed.velocity - new Vec3(-327.06262f, -112.45098f, 853.15137f)).Length(), 0f, 0.5f);
        Assert.InRange((seed.position - new Vec3(900.9274f, -1038.6968f, -148.32233f)).Length(), 0f, 0.1f);
    }
}
