using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using Xunit;

namespace FiveStack.Tests;

// Pinned against web/composables/useRadarProjection.ts. If these move, the panel
// and the HUD have stopped agreeing about where a lineup lands.
public class RadarProjectionTests
{
    private static RadarCalibration Mirage()
    {
        return new RadarCalibration
        {
            Resolution = 5.02f,
            OffsetX = 3240f,
            OffsetY = 3410f,
        };
    }

    private static RadarCalibration Nuke()
    {
        return new RadarCalibration
        {
            Resolution = 6.98f,
            OffsetX = 3290f,
            OffsetY = 5990f,
            Splits = new List<RadarSplit>
            {
                new RadarSplit
                {
                    BoundsTop = -482f,
                    BoundsBottom = -2500f,
                    OffsetX = 0f,
                    OffsetY = -46f,
                },
            },
        };
    }

    [Theory]
    [InlineData("de_mirage", "de_mirage")]
    [InlineData("DE_MIRAGE", "de_mirage")]
    [InlineData("  de_inferno  ", "de_inferno")]
    [InlineData("de_ancient_night", "de_ancient")]
    [InlineData(null, "")]
    public void MapNamesNormaliseLikeTheWeb(string? input, string expected)
    {
        Assert.Equal(expected, RadarProjection.NormalizeMapName(input));
    }

    // x = (world.x + offsetX) / resolution
    // y = 1024 - (world.y + offsetY) / resolution
    [Fact]
    public void ProjectsWithTheSameArithmeticAsTheWeb()
    {
        (float x, float y) = RadarProjection.Project(new Vec3(0f, 0f, 0f), Mirage());

        Assert.Equal(3240f / 5.02f, x, 2);
        Assert.Equal(1024f - 3410f / 5.02f, y, 2);
    }

    // The y flip is the easiest thing to get backwards, and getting it backwards
    // mirrors every marker about the middle of the map.
    [Fact]
    public void NorthIsUp()
    {
        RadarCalibration mirage = Mirage();
        (_, float low) = RadarProjection.Project(new Vec3(0f, -1000f, 0f), mirage);
        (_, float high) = RadarProjection.Project(new Vec3(0f, 1000f, 0f), mirage);

        Assert.True(high < low);
    }

    [Fact]
    public void EastIsRight()
    {
        RadarCalibration mirage = Mirage();
        (float left, _) = RadarProjection.Project(new Vec3(-1000f, 0f, 0f), mirage);
        (float right, _) = RadarProjection.Project(new Vec3(1000f, 0f, 0f), mirage);

        Assert.True(right > left);
    }

    [Fact]
    public void NukeLowerLevelShiftsOntoItsOwnHalf()
    {
        RadarCalibration nuke = Nuke();
        var upper = new Vec3(0f, 0f, -100f);
        var lower = new Vec3(0f, 0f, -1000f);

        (_, float upperY) = RadarProjection.Project(upper, nuke);
        (_, float lowerY) = RadarProjection.Project(lower, nuke);

        // -46% of the image on a y that is then flipped, so the lower level ends
        // up BELOW the upper one on screen.
        Assert.Equal(upperY + 0.46f * RadarProjection.Pixels, lowerY, 2);
    }

    [Fact]
    public void HeightOutsideTheSplitBandIsUnshifted()
    {
        RadarCalibration nuke = Nuke();

        Assert.Equal((0f, 0f), RadarProjection.Split(-100f, nuke.Splits));
        Assert.Equal((0f, -46f), RadarProjection.Split(-1000f, nuke.Splits));
        Assert.Equal((0f, 0f), RadarProjection.Split(-3000f, nuke.Splits));
    }

    [Fact]
    public void NormalisedStaysOnTheImage()
    {
        RadarCalibration mirage = Mirage();

        foreach (float world in new[] { -99999f, -2000f, 0f, 2000f, 99999f })
        {
            (float x, float y) = RadarProjection.Normalized(new Vec3(world, world, 0f), mirage);

            Assert.InRange(x, 0f, 1f);
            Assert.InRange(y, 0f, 1f);
        }
    }

    // The shipped file, parsed by the shipped loader. A calibration that fails
    // to parse means the minimap silently has no radar for that map.
    [Fact]
    public void TheShippedCalibrationParses()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        string? path = null;

        while (directory != null && path == null)
        {
            string candidate = Path.Combine(
                directory.FullName, "apps", "utility-sw", "hud", "radars", "metadata.json"
            );

            if (File.Exists(candidate))
            {
                path = candidate;
            }

            directory = directory.Parent;
        }

        Assert.NotNull(path);

        Dictionary<string, RadarCalibration> loaded = RadarMaps.Load(path!);

        foreach (string map in RadarMaps.All)
        {
            Assert.True(loaded.ContainsKey(map), $"{map}: did not parse out of metadata.json");
            Assert.True(loaded[map].Resolution > 0f, $"{map}: resolution is zero");
        }

        Assert.Equal(2, loaded["de_nuke"].Splits.Count + loaded["de_vertigo"].Splits.Count);
    }

    [Fact]
    public void CellsLandInsideTheGrid()
    {
        RadarCalibration mirage = Mirage();

        for (float world = -4000f; world <= 4000f; world += 250f)
        {
            (int column, int row) = RadarProjection.Cell(
                new Vec3(world, world, 0f),
                mirage,
                64,
                64
            );

            Assert.InRange(column, 0, 63);
            Assert.InRange(row, 0, 63);
        }
    }
}
