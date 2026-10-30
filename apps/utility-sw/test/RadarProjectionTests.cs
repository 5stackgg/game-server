using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using Xunit;

namespace FiveStack.Tests;

// Pinned against CS2's resource/overviews/<map>.txt. If these move, markers no
// longer land where CS2's own radar image draws the place.
public class RadarProjectionTests
{
    private static string ShippedCalibration()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            string candidate = Path.Combine(
                directory.FullName, "apps", "utility-sw", "hud", "radars", "metadata.json"
            );

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("apps/utility-sw/hud/radars/metadata.json not found above the test output");
    }

    private static Dictionary<string, RadarCalibration> Shipped()
    {
        return RadarMaps.Load(ShippedCalibration());
    }

    private static RadarCalibration Mirage()
    {
        return new RadarCalibration
        {
            Resolution = 5f,
            OffsetX = 3230f,
            OffsetY = 1024f * 5f - 1713f,
        };
    }

    private static RadarCalibration Nuke()
    {
        return new RadarCalibration
        {
            Resolution = 7f,
            OffsetX = 3453f,
            OffsetY = 1024f * 7f - 2887f,
            Levels = new List<RadarLevel> { new RadarLevel { Name = "lower", MaxZ = -495f } },
        };
    }

    [Theory]
    [InlineData("de_mirage", "de_mirage")]
    [InlineData("DE_MIRAGE", "de_mirage")]
    [InlineData("  de_inferno  ", "de_inferno")]
    [InlineData("de_ancient_night", "de_ancient")]
    [InlineData(null, "")]
    public void MapNamesNormalise(string? input, string expected)
    {
        Assert.Equal(expected, RadarProjection.NormalizeMapName(input));
    }

    // pos_x, pos_y and scale exactly as each overview file states them. The
    // overview's top left corner is pixel (0, 0), and anything else is
    // ((x - pos_x) / scale, (pos_y - y) / scale) on the 1024 square image.
    [Theory]
    [InlineData("de_ancient", -2953f, 2164f, 5f)]
    [InlineData("de_anubis", -2796f, 3328f, 5.22f)]
    [InlineData("de_cache", -2000f, 3250f, 5.5f)]
    [InlineData("de_dust2", -2476f, 3239f, 4.4f)]
    [InlineData("de_inferno", -2087f, 3870f, 4.9f)]
    [InlineData("de_mirage", -3230f, 1713f, 5f)]
    [InlineData("de_nuke", -3453f, 2887f, 7f)]
    [InlineData("de_overpass", -4831f, 1781f, 5.2f)]
    [InlineData("de_train", -2308f, 2078f, 4.082077f)]
    [InlineData("de_vertigo", -3168f, 1762f, 4f)]
    public void ShippedCalibrationIsValvesOverview(string map, float posX, float posY, float scale)
    {
        RadarCalibration calibration = Shipped()[map];

        (float cornerX, float cornerY) = RadarProjection.Project(new Vec3(posX, posY, 0f), calibration);

        Assert.Equal(0f, cornerX, 2);
        Assert.Equal(0f, cornerY, 2);

        var point = new Vec3(-512f, 768f, 0f);
        (float x, float y) = RadarProjection.Project(point, calibration);

        Assert.Equal((point.x - posX) / scale, x, 2);
        Assert.Equal((posY - point.y) / scale, y, 2);
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

    [Theory]
    [InlineData(-300f, null)]
    [InlineData(-495f, null)]
    [InlineData(-496f, "lower")]
    [InlineData(-770f, "lower")]
    public void NukeIsOnItsLowerLevelBelowValvesAltitude(float z, string? expected)
    {
        Assert.Equal(expected, RadarProjection.Level(z, Nuke()));
    }

    [Fact]
    public void AMapWithoutLevelsIsAlwaysOnItsMainImage()
    {
        Assert.Null(RadarProjection.Level(-5000f, Mirage()));
        Assert.Null(RadarProjection.Level(5000f, Mirage()));
    }

    [Fact]
    public void APointIsOnTheNearestLevelAboveIt()
    {
        var calibration = new RadarCalibration
        {
            Resolution = 5f,
            Levels = new List<RadarLevel>
            {
                new RadarLevel { Name = "lower", MaxZ = 0f },
                new RadarLevel { Name = "basement", MaxZ = -500f },
            },
        };

        Assert.Null(RadarProjection.Level(100f, calibration));
        Assert.Equal("lower", RadarProjection.Level(-100f, calibration));
        Assert.Equal("basement", RadarProjection.Level(-600f, calibration));
    }

    // Valve draws a lower floor on its own image in the same frame, so the
    // height picks the image and never moves the point.
    [Fact]
    public void ALowerLevelProjectsInTheSameFrame()
    {
        RadarCalibration nuke = Nuke();

        Assert.Equal(
            RadarProjection.Project(new Vec3(600f, -900f, -400f), nuke),
            RadarProjection.Project(new Vec3(600f, -900f, -760f), nuke)
        );
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
        Dictionary<string, RadarCalibration> loaded = Shipped();

        foreach (string map in RadarMaps.All)
        {
            Assert.True(loaded.ContainsKey(map), $"{map}: did not parse out of metadata.json");
            Assert.True(loaded[map].Resolution > 0f, $"{map}: resolution is zero");
        }

        Assert.Equal(RadarMaps.All.Count, loaded.Count);
    }

    // verticalsections, as each overview file states it: the lower image is
    // drawn below its AltitudeMax.
    [Theory]
    [InlineData("de_nuke", -495f)]
    [InlineData("de_train", -50f)]
    [InlineData("de_vertigo", 11700f)]
    public void ShippedLowerLevelsAreValves(string map, float maxZ)
    {
        RadarLevel level = Assert.Single(Shipped()[map].Levels);

        Assert.Equal("lower", level.Name);
        Assert.Equal(maxZ, level.MaxZ);
    }

    [Fact]
    public void OnlyNukeTrainAndVertigoHaveALowerLevel()
    {
        string[] levelled = Shipped()
            .Where(entry => entry.Value.Levels.Count > 0)
            .Select(entry => entry.Key)
            .OrderBy(map => map, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "de_nuke", "de_train", "de_vertigo" }, levelled);
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
