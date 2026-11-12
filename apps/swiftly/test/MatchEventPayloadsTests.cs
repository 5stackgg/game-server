using System.Text.Json;
using FiveStack.Enums;
using FiveStack.Utilities;
using Xunit;

// The api reads the replay off the top of the event's data, and only on Warmup.
// Anything else goes out exactly as it did before the marker.
public class MatchEventPayloadsTests
{
    private static readonly Guid Map = Guid.NewGuid();

    private static readonly DateTime At = new(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);

    private static JsonElement Sent(Dictionary<string, object> data)
    {
        return JsonDocument.Parse(JsonSerializer.Serialize(data)).RootElement;
    }

    [Fact]
    public void AReplayWarmupSaysSo()
    {
        JsonElement sent = Sent(
            MatchEventPayloads.MapStatus(eMapStatus.Warmup, null, At, Map, replay: true)
        );

        Assert.Equal("Warmup", sent.GetProperty("status").GetString());
        Assert.True(sent.GetProperty("replay").GetBoolean());
        Assert.Equal(Map.ToString(), sent.GetProperty("match_map_id").GetString());
    }

    [Theory]
    [InlineData(eMapStatus.Warmup, false)]
    [InlineData(eMapStatus.Live, false)]
    [InlineData(eMapStatus.Live, true)]
    [InlineData(eMapStatus.Paused, true)]
    public void AnyOtherStatusGoesOutAsBefore(eMapStatus status, bool replay)
    {
        Dictionary<string, object> data = MatchEventPayloads.MapStatus(
            status,
            null,
            At,
            Map,
            replay
        );

        Assert.Equal(
            new[] { "status", "winning_lineup_id", "time", "match_map_id" },
            data.Keys.ToArray()
        );
    }

    [Fact]
    public void AStatusWithNoActiveMapNamesNone()
    {
        Assert.DoesNotContain(
            "match_map_id",
            MatchEventPayloads.MapStatus(eMapStatus.Warmup, null, At, null).Keys
        );
    }

    [Fact]
    public void AReplayResetSaysSo()
    {
        JsonElement sent = Sent(MatchEventPayloads.RestoreRound(0, Map, replay: true));

        Assert.Equal(0, sent.GetProperty("round").GetInt32());
        Assert.Equal(Map.ToString(), sent.GetProperty("match_map_id").GetString());
        Assert.True(sent.GetProperty("replay").GetBoolean());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void AnOrdinaryResetGoesOutAsBefore(int round)
    {
        Assert.Equal(
            new[] { "round", "match_map_id" },
            MatchEventPayloads.RestoreRound(round, Map).Keys.ToArray()
        );
    }
}
