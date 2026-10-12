using System.Globalization;
using System.Text.RegularExpressions;
using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using Xunit;

// A contract with another process rather than with a person. Every assertion
// here is a thing an external clip recorder would break on, which is why they
// are pinned rather than left to whatever the formatter happens to do.
public class PracticeSignalUtilityTests
{
    // The shape a reader outside this repo matches on.
    private static readonly Regex Line = new Regex(
        @"^\[utility-practice\] ghost_detonated utility=(?<utility>\S+) lineup=(?<lineup>\S+) lineup_id=(?<lineup_id>\S+) steam=(?<steam>\d+) x=(?<x>-?\d+\.\d\d) y=(?<y>-?\d+\.\d\d) z=(?<z>-?\d+\.\d\d)$"
    );

    [Fact]
    public void TheDetonationLineHasTheShapeAReaderExpects()
    {
        string line = PracticeSignalUtility.GhostDetonatedLine(
            "Smoke",
            new Vec3(-1234.5f, 567.25f, 64f),
            "client-1",
            "panel-1",
            76561198000000001
        );

        Match match = Line.Match(line);

        Assert.True(match.Success, line);
        Assert.Equal("Smoke", match.Groups["utility"].Value);
        Assert.Equal("client-1", match.Groups["lineup"].Value);
        Assert.Equal("panel-1", match.Groups["lineup_id"].Value);
        Assert.Equal("76561198000000001", match.Groups["steam"].Value);
        Assert.Equal("-1234.50", match.Groups["x"].Value);
        Assert.Equal("567.25", match.Groups["y"].Value);
        Assert.Equal("64.00", match.Groups["z"].Value);
    }

    // A lineup thrown from throw history has no panel id yet. The field stays
    // present so a reader can key on names and not on how many fields there
    // happen to be this time.
    [Fact]
    public void AnAbsentIdIsStillAField()
    {
        string line = PracticeSignalUtility.GhostDetonatedLine(
            "Molotov",
            new Vec3(0f, 0f, 0f),
            "client-1",
            null,
            1
        );

        Assert.True(Line.IsMatch(line), line);
        Assert.Contains("lineup_id=-", line);
    }

    // A server running under a locale where the decimal separator is a comma
    // would otherwise emit "x=-1234,50" and split every reader's parser.
    [Fact]
    public void TheLineIsTheSameInEveryLocale()
    {
        CultureInfo original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            string line = PracticeSignalUtility.GhostDetonatedLine(
                "Flash",
                new Vec3(-1234.5f, 567.25f, 64f),
                "client-1",
                "panel-1",
                7
            );

            Assert.Contains("x=-1234.50", line);
            Assert.True(Line.IsMatch(line), line);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void TheLineIsOneLineAndSpaceSeparable()
    {
        string line = PracticeSignalUtility.GhostDetonatedLine(
            "HighExplosive",
            new Vec3(1f, 2f, 3f),
            "a name with spaces",
            "panel-1",
            9
        );

        Assert.DoesNotContain("\n", line);
        Assert.Contains("lineup=a_name_with_spaces", line);
        Assert.True(Line.IsMatch(line), line);
    }
}
