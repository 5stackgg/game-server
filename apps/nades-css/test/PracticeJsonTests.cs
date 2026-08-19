using System.Text.Json;
using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using Xunit;

public class PracticeJsonTests
{
    [Fact]
    public void ASessionReadsBackFromThePanelsSpelling()
    {
        const string json =
            "{\"id\":\"11111111-1111-1111-1111-111111111111\",\"match_id\":\"22222222-2222-2222-2222-222222222222\",\"password\":\"pw\",\"map\":\"de_mirage\",\"allowed_steam_ids\":[\"1\",\"2\"]}";

        PracticeSessionData? session = JsonSerializer.Deserialize<PracticeSessionData>(
            json,
            PracticeJson.Options
        );

        Assert.NotNull(session);
        Assert.Equal("pw", session!.password);
        Assert.Equal("de_mirage", session.map);
        Assert.Equal(2, session.allowed_steam_ids.Count);
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), session.match_id);
    }

    [Fact]
    public void APathPointReadsBackFieldByField()
    {
        const string json = "[{\"tick\":7,\"x\":1.5,\"y\":-2.5,\"z\":3}]";

        List<NadePathPoint>? path = JsonSerializer.Deserialize<List<NadePathPoint>>(
            json,
            PracticeJson.Options
        );

        NadePathPoint point = Assert.Single(path!);
        Assert.Equal(7, point.tick);
        Assert.Equal(1.5f, point.x);
        Assert.Equal(-2.5f, point.y);
        Assert.Equal(3f, point.z);
    }

    // The panel decides which fields a row carries, so a partial row must read
    // rather than throw.
    [Fact]
    public void AMissingFieldReadsAsAbsentRatherThanFailing()
    {
        const string json = "{\"id\":\"x\",\"name\":\"only a name\"}";

        NadeLibraryRow? row = JsonSerializer.Deserialize<NadeLibraryRow>(
            json,
            PracticeJson.Options
        );

        Assert.NotNull(row);
        Assert.Null(row!.origin_x);

        LineupRecord lineup = row.ToLineup();
        Assert.Equal(0f, lineup.release.feet_position.x);
        Assert.Equal(0f, lineup.flight_time);
    }
}
