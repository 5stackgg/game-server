using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using Xunit;

// The bloom outline is the only thing in the plugin that turns bytes into
// entities, so these pin both halves of it: the packing the parser chose, and
// the entity budget that keeps a smoke from spawning a thousand beams.
public class SmokeVolumeUtilityTests
{
    private static SmokeVolume Volume(
        int dx,
        int dy,
        int dz,
        byte[]? cells = null,
        float vs = 8f,
        float ox = 0f,
        float oy = 0f,
        float oz = 0f
    )
    {
        return new SmokeVolume
        {
            ox = ox,
            oy = oy,
            oz = oz,
            vs = vs,
            dx = dx,
            dy = dy,
            dz = dz,
            den = cells == null ? null : Encode(cells),
        };
    }

    // Two cells per byte, low nibble first.
    private static string Encode(byte[] cells)
    {
        var packed = new byte[(cells.Length + 1) / 2];

        for (int index = 0; index < cells.Length; index++)
        {
            byte value = (byte)(cells[index] & 0x0F);

            if ((index & 1) == 0)
            {
                packed[index >> 1] |= value;
            }
            else
            {
                packed[index >> 1] |= (byte)(value << 4);
            }
        }

        return System.Convert.ToBase64String(packed);
    }

    [Fact]
    public void TheLowNibbleOfAByteIsTheFirstCell()
    {
        SmokeVolume volume = Volume(2, 1, 1);
        volume.den = System.Convert.ToBase64String(new byte[] { 0xF0 });

        byte[] density = SmokeVolumeUtility.Decode(volume);

        Assert.Equal(0, SmokeVolumeUtility.Density(density, volume, 0, 0, 0));
        Assert.Equal(15, SmokeVolumeUtility.Density(density, volume, 1, 0, 0));
    }

    [Fact]
    public void CellsAreOrderedXMajorThenYThenZ()
    {
        SmokeVolume volume = Volume(2, 2, 2, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        byte[] density = SmokeVolumeUtility.Decode(volume);

        Assert.Equal(1, SmokeVolumeUtility.Density(density, volume, 0, 0, 0));
        Assert.Equal(2, SmokeVolumeUtility.Density(density, volume, 1, 0, 0));
        Assert.Equal(3, SmokeVolumeUtility.Density(density, volume, 0, 1, 0));
        Assert.Equal(4, SmokeVolumeUtility.Density(density, volume, 1, 1, 0));
        Assert.Equal(5, SmokeVolumeUtility.Density(density, volume, 0, 0, 1));
        Assert.Equal(8, SmokeVolumeUtility.Density(density, volume, 1, 1, 1));
    }

    [Fact]
    public void ACellOutsideTheGridIsClearRatherThanAnError()
    {
        SmokeVolume volume = Volume(2, 2, 2, new byte[] { 9, 9, 9, 9, 9, 9, 9, 9 });
        byte[] density = SmokeVolumeUtility.Decode(volume);

        Assert.Equal(0, SmokeVolumeUtility.Density(density, volume, -1, 0, 0));
        Assert.Equal(0, SmokeVolumeUtility.Density(density, volume, 2, 0, 0));
        Assert.Equal(0, SmokeVolumeUtility.Density(density, volume, 0, 0, 2));
    }

    // The array is optional in the contract; the box is then the measurement.
    [Fact]
    public void AVolumeWithNoGridIsSolid()
    {
        SmokeVolume volume = Volume(3, 3, 3);
        byte[] density = SmokeVolumeUtility.Decode(volume);

        Assert.Equal(27, density.Length);
        Assert.All(density, cell => Assert.Equal(15, cell));
    }

    [Fact]
    public void ADenShorterThanTheGridLeavesTheRestClear()
    {
        SmokeVolume volume = Volume(4, 1, 1);
        volume.den = System.Convert.ToBase64String(new byte[] { 0x21 });

        byte[] density = SmokeVolumeUtility.Decode(volume);

        Assert.Equal(1, SmokeVolumeUtility.Density(density, volume, 0, 0, 0));
        Assert.Equal(2, SmokeVolumeUtility.Density(density, volume, 1, 0, 0));
        Assert.Equal(0, SmokeVolumeUtility.Density(density, volume, 2, 0, 0));
        Assert.Equal(0, SmokeVolumeUtility.Density(density, volume, 3, 0, 0));
    }

    [Fact]
    public void GarbageBase64DecodesToNothingRatherThanThrowing()
    {
        SmokeVolume volume = Volume(2, 2, 1);
        volume.den = "this is not base64 !!";

        byte[] density = SmokeVolumeUtility.Decode(volume);

        Assert.Equal(4, density.Length);
        Assert.All(density, cell => Assert.Equal(0, cell));
    }

    [Fact]
    public void AGridBiggerThanTheCellCeilingIsRefused()
    {
        SmokeVolume volume = Volume(512, 512, 512);

        Assert.Empty(SmokeVolumeUtility.Decode(volume));
        Assert.Empty(SmokeVolumeUtility.Outline(volume));
    }

    [Fact]
    public void NoVolumeOutlinesToNothing()
    {
        Assert.Empty(SmokeVolumeUtility.Outline(null));
    }

    [Fact]
    public void AnEmptyGridOutlinesToNothing()
    {
        SmokeVolume volume = Volume(4, 4, 4, new byte[64]);

        Assert.Empty(SmokeVolumeUtility.Outline(volume));
    }

    [Fact]
    public void ASolidBoxOutlinesToOneRectanglePerLevel()
    {
        SmokeVolume volume = Volume(4, 4, 4, ox: 100f, oy: 200f, oz: 300f);

        List<BloomSegment> segments = SmokeVolumeUtility.Outline(volume);

        Assert.Equal(12, segments.Count);

        foreach (BloomSegment segment in segments)
        {
            Assert.InRange(segment.a.x, 100f, 132f);
            Assert.InRange(segment.a.y, 200f, 232f);
            Assert.InRange(segment.a.z, 300f, 332f);
            Assert.InRange(segment.b.x, 100f, 132f);
            Assert.InRange(segment.b.y, 200f, 232f);
        }
    }

    [Fact]
    public void EveryLevelOfTheOutlineSitsAtADifferentHeight()
    {
        SmokeVolume volume = Volume(4, 4, 4);

        var heights = SmokeVolumeUtility
            .Outline(volume)
            .Select(segment => segment.a.z)
            .Distinct()
            .ToList();

        Assert.Equal(3, heights.Count);
    }

    [Fact]
    public void ASingleLayerOnlyDrawsOneContour()
    {
        SmokeVolume volume = Volume(4, 4, 1);

        List<BloomSegment> segments = SmokeVolumeUtility.Outline(volume);

        Assert.Equal(4, segments.Count);
    }

    // A pillar inside the bloom is the thing a player is looking for, so the
    // hole gets its own loop rather than being swallowed by the silhouette.
    [Fact]
    public void AHoleInTheBloomIsOutlinedToo()
    {
        var cells = new byte[7 * 7];

        for (int index = 0; index < cells.Length; index++)
        {
            cells[index] = 15;
        }

        for (int j = 2; j <= 4; j++)
        {
            for (int i = 2; i <= 4; i++)
            {
                cells[(j * 7) + i] = 0;
            }
        }

        SmokeVolume volume = Volume(7, 7, 1, cells);
        List<BloomSegment> segments = SmokeVolumeUtility.Outline(volume);

        Assert.Equal(8, segments.Count);
    }

    // One stray dense cell is measurement noise, not somewhere to throw at.
    [Fact]
    public void ASingleStrayCellIsNotWorthABeam()
    {
        var cells = new byte[5 * 5];
        cells[(2 * 5) + 2] = 15;

        SmokeVolume volume = Volume(5, 5, 1, cells);

        Assert.Empty(SmokeVolumeUtility.Outline(volume));
    }

    [Fact]
    public void TheEntityBudgetIsNeverExceeded()
    {
        SmokeVolume volume = Sphere(18, 8f);

        foreach (int budget in new[] { 4, 8, 16, 48, 96 })
        {
            List<BloomSegment> segments = SmokeVolumeUtility.Outline(
                volume,
                new SmokeOutlineOptions { MaxSegments = budget }
            );

            Assert.True(
                segments.Count <= budget,
                $"{segments.Count} segments for a budget of {budget}"
            );
        }
    }

    [Fact]
    public void ARealisticBloomStillDrawsSomething()
    {
        List<BloomSegment> segments = SmokeVolumeUtility.Outline(Sphere(18, 8f));

        Assert.NotEmpty(segments);
        Assert.All(
            segments,
            segment =>
                Assert.True(
                    (segment.b - segment.a).Length() > 0f,
                    "a zero length beam draws nothing and still costs an entity"
                )
        );
    }

    [Fact]
    public void ADenserThresholdOutlinesASmallerShape()
    {
        SmokeVolume volume = Sphere(18, 8f, falloff: true);

        int wide = SmokeVolumeUtility
            .Outline(volume, new SmokeOutlineOptions { MinDensity = 1, MaxLevels = 1 })
            .Sum(segment => (int)(segment.b - segment.a).LengthXY());

        int tight = SmokeVolumeUtility
            .Outline(volume, new SmokeOutlineOptions { MinDensity = 12, MaxLevels = 1 })
            .Sum(segment => (int)(segment.b - segment.a).LengthXY());

        Assert.True(tight < wide, $"{tight} is not tighter than {wide}");
    }

    // A flood filled bloom clipped by a wall must never be reported as covering
    // the wall: the outline is an exact staircase, simplified inwards only by
    // the epsilon, so nothing is drawn past the last occupied cell.
    [Fact]
    public void TheOutlineStaysInsideTheMeasuredCells()
    {
        var cells = new byte[8 * 8];

        for (int j = 0; j < 8; j++)
        {
            for (int i = 0; i < 4; i++)
            {
                cells[(j * 8) + i] = 15;
            }
        }

        SmokeVolume volume = Volume(8, 8, 1, cells);

        foreach (BloomSegment segment in SmokeVolumeUtility.Outline(volume))
        {
            Assert.InRange(segment.a.x, 0f, 32f);
            Assert.InRange(segment.b.x, 0f, 32f);
        }
    }

    private static SmokeVolume Sphere(int diameter, float vs, bool falloff = false)
    {
        var cells = new byte[diameter * diameter * diameter];
        float radius = diameter / 2f;

        for (int k = 0; k < diameter; k++)
        {
            for (int j = 0; j < diameter; j++)
            {
                for (int i = 0; i < diameter; i++)
                {
                    float dx = i - radius + 0.5f;
                    float dy = j - radius + 0.5f;
                    float dz = k - radius + 0.5f;
                    float distance = MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

                    if (distance > radius)
                    {
                        continue;
                    }

                    cells[(((k * diameter) + j) * diameter) + i] = falloff
                        ? (byte)Math.Clamp((int)(15f * (1f - (distance / radius))), 1, 15)
                        : (byte)15;
                }
            }
        }

        SmokeVolume volume = Volume(diameter, diameter, diameter, cells, vs);
        return volume;
    }
}
