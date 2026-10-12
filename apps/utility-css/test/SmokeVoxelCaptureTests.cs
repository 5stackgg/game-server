using System.Text.Json;
using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using Xunit;

// The parser rebuilds the cloud from these reads exactly as it would from a
// demo's, and the panel keeps the first cloud a lineup gets, so every case here
// is one that would store the wrong cloud rather than fail: a byte out of
// order, a tick that runs backwards, or a cloud handed over half grown.
public class SmokeVoxelCaptureTests
{
    private static byte[] Bytes(int from, int count)
    {
        return Enumerable.Range(from, count).Select(value => (byte)value).ToArray();
    }

    // Long enough after the last growth that any cloud has settled.
    private static SmokeVoxelPayload? Settle(SmokeVoxelCapture capture)
    {
        capture.Check(SmokeVoxelCapture.MaxTicks * 2);

        return capture.ToPayload();
    }

    [Fact]
    public void EachGrowthIsStampedWithItsLengthAndTick()
    {
        var capture = new SmokeVoxelCapture();

        capture.Append(Bytes(0, 10), 0);
        capture.Append(Bytes(10, 6), 5);
        capture.Append(Bytes(16, 4), 9);

        SmokeVoxelPayload? payload = Settle(capture);

        Assert.NotNull(payload);
        Assert.Equal(new[] { new[] { 10, 0 }, new[] { 16, 5 }, new[] { 20, 9 } }, payload.samples);
        Assert.Equal(Bytes(0, 20), Convert.FromBase64String(payload.log));
        Assert.Equal(SmokeVoxelCapture.TickRate, payload.tick_rate);
    }

    [Fact]
    public void TwoReadsOnOneTickAreOneArrival()
    {
        var capture = new SmokeVoxelCapture();

        capture.Append(Bytes(0, 8), 0);
        capture.Append(Bytes(8, 8), 0);

        Assert.Equal(new[] { new[] { 16, 0 } }, Settle(capture)!.samples);
    }

    [Fact]
    public void TicksNeverRunBackwards()
    {
        var capture = new SmokeVoxelCapture();

        capture.Append(Bytes(0, 4), 7);
        capture.Append(Bytes(4, 4), 3);

        Assert.Equal(new[] { new[] { 8, 7 } }, Settle(capture)!.samples);
    }

    [Fact]
    public void OnlyGrowthIsNew()
    {
        var capture = new SmokeVoxelCapture();

        capture.Append(Bytes(0, 100), 0);

        Assert.Equal(0, capture.Growth(100));
        Assert.Equal(0, capture.Growth(40));
        Assert.Equal(20, capture.Growth(120));
    }

    [Fact]
    public void NothingPastTheNetworkedArrayIsKept()
    {
        var capture = new SmokeVoxelCapture();

        Assert.Equal(SmokeVoxelCapture.MaxBytes, capture.Growth(SmokeVoxelCapture.MaxBytes + 500));

        capture.Append(new byte[SmokeVoxelCapture.MaxBytes - 10], 0);
        capture.Append(new byte[64], 4);

        Assert.Equal(SmokeVoxelCapture.MaxBytes, capture.Length);
        Assert.Equal(eSmokeVoxelEnd.Full, capture.Check(4));
        Assert.Equal(SmokeVoxelCapture.MaxBytes, capture.ToPayload()!.samples[^1][0]);
    }

    [Fact]
    public void ACloudStillGrowingIsNotFinished()
    {
        var capture = new SmokeVoxelCapture();

        capture.Append(Bytes(0, 10), 0);
        capture.Append(Bytes(10, 10), 200);

        Assert.Equal(eSmokeVoxelEnd.Growing, capture.Check(200 + SmokeVoxelCapture.QuietTicks - 1));
        Assert.Null(capture.ToPayload());
    }

    [Fact]
    public void ACloudThatStopsGrowingIsSettled()
    {
        var capture = new SmokeVoxelCapture();

        capture.Append(Bytes(0, 10), 0);
        capture.Append(Bytes(10, 10), 100);

        Assert.Equal(eSmokeVoxelEnd.Settled, capture.Check(100 + SmokeVoxelCapture.QuietTicks));
        Assert.NotNull(capture.ToPayload());
    }

    [Fact]
    public void ASmokeThatNeverWritesSettlesFromItsDetonation()
    {
        var capture = new SmokeVoxelCapture();

        Assert.Equal(eSmokeVoxelEnd.Growing, capture.Check(SmokeVoxelCapture.QuietTicks - 1));
        Assert.Equal(eSmokeVoxelEnd.Settled, capture.Check(SmokeVoxelCapture.QuietTicks));
        Assert.Null(capture.ToPayload());
    }

    [Fact]
    public void ACloudThatKeepsGrowingStopsAtTheLimit()
    {
        var capture = new SmokeVoxelCapture();

        for (int tick = 0; tick < SmokeVoxelCapture.MaxTicks; tick += 32)
        {
            capture.Append(Bytes(tick, 2), tick);
        }

        Assert.Equal(eSmokeVoxelEnd.Growing, capture.Check(SmokeVoxelCapture.MaxTicks - 1));
        Assert.Equal(eSmokeVoxelEnd.TimedOut, capture.Check(SmokeVoxelCapture.MaxTicks));
        Assert.NotNull(capture.ToPayload());
    }

    [Fact]
    public void ACloudCutShortIsNeverHandedOver()
    {
        var capture = new SmokeVoxelCapture();

        capture.Append(Bytes(0, 40), 0);
        capture.Append(Bytes(40, 40), 30);
        capture.CutShort();

        Assert.Equal(eSmokeVoxelEnd.CutShort, capture.End);
        Assert.Equal(eSmokeVoxelEnd.CutShort, capture.Check(SmokeVoxelCapture.MaxTicks * 2));
        Assert.Null(capture.ToPayload());
    }

    [Fact]
    public void ACloudThatFinishedIsNotUndoneByItsProjectileGoing()
    {
        var capture = new SmokeVoxelCapture();

        capture.Append(Bytes(0, 40), 0);
        capture.Check(SmokeVoxelCapture.QuietTicks);
        capture.CutShort();

        Assert.Equal(eSmokeVoxelEnd.Settled, capture.End);
        Assert.NotNull(capture.ToPayload());
    }

    [Fact]
    public void ThePayloadIsTheParsersShape()
    {
        var capture = new SmokeVoxelCapture { Detonation = new Vec3(-512.5f, 1024f, 96f) };

        capture.Append(new byte[] { 1, 2, 3 }, 0);
        capture.Append(new byte[] { 4 }, 6);

        string json = JsonSerializer.Serialize(Settle(capture), PracticeJson.Options);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        Assert.Equal("AQIDBA==", root.GetProperty("log").GetString());
        Assert.Equal("[[3,0],[4,6]]", root.GetProperty("samples").GetRawText());
        Assert.Equal(-512.5f, root.GetProperty("detonation").GetProperty("x").GetSingle());
        Assert.Equal(1024f, root.GetProperty("detonation").GetProperty("y").GetSingle());
        Assert.Equal(96f, root.GetProperty("detonation").GetProperty("z").GetSingle());
        Assert.Equal(64, root.GetProperty("tick_rate").GetInt32());
    }

    [Fact]
    public void AnIngestCarriesTheLogOnlyOnceItIsThere()
    {
        var lineup = new LineupRecord { utility_type = "Smoke" };

        string without = JsonSerializer.Serialize(
            UtilityIngestPayload.From(lineup),
            PracticeJson.Options
        );
        Assert.False(JsonDocument.Parse(without).RootElement.TryGetProperty("smoke_voxels", out _));

        var capture = new SmokeVoxelCapture();
        capture.Append(new byte[] { 7 }, 0);
        lineup.smoke_voxels = Settle(capture);

        string with = JsonSerializer.Serialize(
            UtilityIngestPayload.From(lineup),
            PracticeJson.Options
        );
        JsonElement voxels = JsonDocument.Parse(with).RootElement.GetProperty("smoke_voxels");

        Assert.Equal("Bw==", voxels.GetProperty("log").GetString());
        Assert.Equal("[[1,0]]", voxels.GetProperty("samples").GetRawText());
    }
}
