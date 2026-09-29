using FiveStack.Utilities;
using Xunit;

public class LineupCapacityTests
{
    private const int Spectator = 1;
    private const int T = 2;
    private const int CT = 3;
    private const int Capacity = 5;
    private const string Joiner = "joiner";

    private static readonly Guid LineupA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid LineupB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static List<(string SteamId, Guid? LineupId, int TeamNum)> Players(
        Guid? lineupId,
        int teamNum,
        int count,
        string prefix
    )
    {
        return Enumerable
            .Range(0, count)
            .Select(index => ($"{prefix}{index}", lineupId, teamNum))
            .ToList();
    }

    [Fact]
    public void SixthMemberOfAPlayingLineupIsOverCapacity()
    {
        var connected = Players(LineupA, CT, 5, "a");
        connected.Add((Joiner, LineupA, 0));

        Assert.True(
            LineupCapacityUtility.IsOverCapacity(connected, Joiner, LineupA, CT, Capacity)
        );
    }

    [Fact]
    public void LineupStillOnTheOtherSideThroughHalftimeCounts()
    {
        var connected = Players(LineupA, T, 5, "a");
        connected.AddRange(Players(LineupB, CT, 5, "b"));
        connected.Add((Joiner, LineupA, 0));

        Assert.True(
            LineupCapacityUtility.IsOverCapacity(connected, Joiner, LineupA, CT, Capacity)
        );
    }

    [Fact]
    public void FifthMemberFitsWhetherOrNotTheyAreAlreadyPlaced()
    {
        var connected = Players(LineupA, CT, 4, "a");
        connected.Add((Joiner, LineupA, 0));

        Assert.False(
            LineupCapacityUtility.IsOverCapacity(connected, Joiner, LineupA, CT, Capacity)
        );

        connected[^1] = (Joiner, LineupA, CT);

        Assert.False(
            LineupCapacityUtility.IsOverCapacity(connected, Joiner, LineupA, CT, Capacity)
        );
    }

    [Fact]
    public void OtherLineupOnTheSameSideDoesNotCount()
    {
        var connected = Players(LineupB, CT, 5, "b");
        connected.AddRange(Players(LineupA, T, 4, "a"));
        connected.Add((Joiner, LineupA, 0));

        Assert.False(
            LineupCapacityUtility.IsOverCapacity(connected, Joiner, LineupA, CT, Capacity)
        );
    }

    [Fact]
    public void BenchedMembersDoNotCount()
    {
        var connected = Players(LineupA, CT, 4, "a");
        connected.AddRange(Players(LineupA, Spectator, 2, "bench"));
        connected.Add((Joiner, LineupA, 0));

        Assert.False(
            LineupCapacityUtility.IsOverCapacity(connected, Joiner, LineupA, CT, Capacity)
        );
    }

    [Fact]
    public void MemberNotHeadedForTOrCTIsNotOverCapacity()
    {
        var connected = Players(LineupA, CT, 5, "a");
        connected.Add((Joiner, LineupA, 0));

        Assert.False(
            LineupCapacityUtility.IsOverCapacity(connected, Joiner, LineupA, 0, Capacity)
        );
    }

    [Fact]
    public void PlayerOutsideEveryLineupIsNeverOverCapacity()
    {
        var connected = Players(null, CT, 5, "x");
        connected.Add((Joiner, null, 0));

        Assert.False(
            LineupCapacityUtility.IsOverCapacity(connected, Joiner, null, CT, Capacity)
        );
    }

    [Fact]
    public void SpectatorsAreNeverOverCapacity()
    {
        var connected = Players(null, Spectator, 8, "caster");
        connected.Add((Joiner, null, 0));

        Assert.False(
            LineupCapacityUtility.IsOverCapacity(connected, Joiner, null, Spectator, Capacity)
        );
    }
}
