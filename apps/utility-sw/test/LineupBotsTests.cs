using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using Xunit;

namespace FiveStack.Tests;

public class LineupBotsTests
{
    private static LineupRecord Lineup(int bots = 0)
    {
        var lineup = new LineupRecord();

        for (int index = 0; index < bots; index++)
        {
            lineup.bots.Add(new LineupBot { x = index * 100f });
        }

        return lineup;
    }

    private static ThrowSnapshot Standing(float x, float y = 0f, float z = 0f, float yaw = 0f)
    {
        return new ThrowSnapshot { feet_position = new Vec3(x, y, z), yaw = yaw };
    }

    [Fact]
    public void PointingAtALineupWithBotsNamesIt()
    {
        LineupRecord pointedAt = Lineup(2);

        LineupBots.Choice choice = LineupBots.Pick(pointedAt, new[] { Lineup(3), pointedAt });

        Assert.Same(pointedAt, choice.Lineup);
    }

    [Fact]
    public void TheOnlyLineupOnTheSpotNeedsNoPointing()
    {
        LineupRecord only = Lineup(3);

        Assert.Same(only, LineupBots.Pick(null, new[] { only }).Lineup);
    }

    [Fact]
    public void TheOnlyLineupWithBotsOnTheSpotNeedsNoPointingEither()
    {
        LineupRecord only = Lineup(3);
        LineupRecord loaded = Lineup();

        Assert.Same(only, LineupBots.Pick(loaded, new[] { loaded, only, Lineup() }).Lineup);
    }

    [Fact]
    public void TwoWithBotsOnTheSpotWaitForThePlayerToPoint()
    {
        LineupBots.Choice choice = LineupBots.Pick(null, new[] { Lineup(1), Lineup(2), Lineup() });

        Assert.Null(choice.Lineup);
        Assert.Equal(2, choice.Waiting);
    }

    [Fact]
    public void NoBotsAnywhereIsNothingToPick()
    {
        LineupBots.Choice choice = LineupBots.Pick(Lineup(), new[] { Lineup() });

        Assert.Null(choice.Lineup);
        Assert.Equal(0, choice.Waiting);
    }

    [Fact]
    public void OnlyBotsNotAlreadyStandingAreAskedFor()
    {
        var saved = new List<LineupBot>
        {
            new() { x = 0f },
            new() { x = 500f },
        };

        List<LineupBot> missing = LineupBots.Missing(saved, new[] { Standing(10f) });

        Assert.Equal(500f, Assert.Single(missing).x);
    }

    [Fact]
    public void BotsThatBelongToSomethingElseStayOutOfIt()
    {
        var saved = new List<LineupBot>
        {
            new() { x = 0f },
        };

        Assert.Single(LineupBots.Missing(saved, new[] { Standing(900f), Standing(0f, 0f, 500f) }));
    }

    [Theory]
    [InlineData("de_mirage", "de_mirage", true)]
    [InlineData("de_mirage", "de_dust2", false)]
    [InlineData("", "de_dust2", false)]
    public void SpotsOnlyMeanSomethingOnTheMapTheyWerePlacedOn(string from, string to, bool kept)
    {
        Assert.Equal(kept, LineupBots.SpotsSurviveLoad(from, to));
    }

    [Fact]
    public void ASnapshotKeepsWhereTheyStandAndWhichWayTheyFace()
    {
        LineupBot bot = Assert.Single(
            LineupBots.Snapshot(new[] { Standing(1f, 2f, 3f, 90f) }, max: 5)
        );

        Assert.Equal((1f, 2f, 3f, 90f), (bot.x, bot.y, bot.z, bot.yaw));
    }

    [Fact]
    public void ASnapshotStopsAtWhatAServerCanStandUp()
    {
        ThrowSnapshot[] standing = Enumerable.Range(0, 7).Select(index => Standing(index)).ToArray();

        Assert.Equal(5, LineupBots.Snapshot(standing, max: 5).Count);
    }
}
