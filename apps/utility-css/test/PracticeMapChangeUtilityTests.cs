using FiveStack.Utilities;
using Xunit;

// The map change is the one command that takes everyone on the server with it,
// and its arguments arrive over RCON as text. Everything decided from that text
// -- who is being stood on what, and which of the two engine commands loads the
// map -- is pinned here so neither plugin can drift from the other.
public class PracticeMapChangeUtilityTests
{
    [Fact]
    public void ParsesAMapOnItsOwn()
    {
        Assert.True(PracticeMapChangeUtility.TryParse(new[] { "de_mirage" }, out var request));

        Assert.Equal("de_mirage", request.map);
        Assert.False(request.HasLoad);
    }

    [Fact]
    public void StripsTheQuotesTheCommandArrivesWith()
    {
        Assert.True(PracticeMapChangeUtility.TryParse(new[] { "\"de_inferno\"" }, out var request));

        Assert.Equal("de_inferno", request.map);
    }

    [Fact]
    public void ParsesAQueuedLoad()
    {
        Assert.True(
            PracticeMapChangeUtility.TryParse(
                new[] { "de_nuke", "76561198000000001", "a,b,c" },
                out var request
            )
        );

        Assert.Equal("de_nuke", request.map);
        Assert.Equal(76561198000000001ul, request.steam_id);
        Assert.Equal(new[] { "a", "b", "c" }, request.lineup_ids);
        Assert.True(request.HasLoad);
    }

    [Fact]
    public void RejectsAnEmptyMap()
    {
        Assert.False(PracticeMapChangeUtility.TryParse(new[] { "\"\"" }, out _));
        Assert.False(PracticeMapChangeUtility.TryParse(Array.Empty<string>(), out _));
    }

    // A steam id that will not parse is a caller bug, not a map change with no
    // load: taking the map half anyway would leave somebody staring at a level
    // they never asked for.
    [Fact]
    public void RejectsAnUnreadableSteamId()
    {
        Assert.False(
            PracticeMapChangeUtility.TryParse(new[] { "de_nuke", "nobody", "a" }, out _)
        );
    }

    [Fact]
    public void AMapWithNoIdsIsStillAMapChange()
    {
        Assert.True(
            PracticeMapChangeUtility.TryParse(
                new[] { "de_nuke", "76561198000000001", "," },
                out var request
            )
        );

        Assert.Equal("de_nuke", request.map);
        Assert.False(request.HasLoad);
    }

    [Fact]
    public void OfficialMapsChangeLevel()
    {
        Assert.Equal("changelevel \"de_mirage\"", PracticeMapChangeUtility.Command("de_mirage"));
    }

    // changelevel cannot load a workshop map, which is why the panel sends the
    // id rather than the name for one.
    [Fact]
    public void WorkshopIdsLoadThroughTheWorkshop()
    {
        Assert.Equal(
            "host_workshop_map 3070315843",
            PracticeMapChangeUtility.Command("3070315843")
        );
    }

    [Fact]
    public void APendingLoadExistsOnlyWhenThereIsOneToMake()
    {
        DateTime now = new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);

        PracticeMapChangeUtility.TryParse(new[] { "de_mirage" }, out var bare);
        Assert.Null(PracticeMapChangeUtility.PendingFor(bare, now));

        PracticeMapChangeUtility.TryParse(
            new[] { "de_mirage", "76561198000000001", "a" },
            out var withLoad
        );

        var pending = PracticeMapChangeUtility.PendingFor(withLoad, now);

        Assert.NotNull(pending);
        Assert.Equal(76561198000000001ul, pending!.steam_id);
        Assert.Equal(
            now.AddSeconds(PracticeMapChangeUtility.PendingSeconds),
            pending.expires_at
        );
    }

    // Somebody whose game did not survive the load is not owed a teleport out
    // of nowhere when they come back.
    [Fact]
    public void PendingLoadsExpire()
    {
        DateTime now = new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);

        PracticeMapChangeUtility.TryParse(
            new[] { "de_mirage", "76561198000000001", "a" },
            out var request
        );

        var pending = PracticeMapChangeUtility.PendingFor(request, now);

        Assert.False(PracticeMapChangeUtility.IsExpired(pending, now.AddSeconds(30)));
        Assert.True(
            PracticeMapChangeUtility.IsExpired(
                pending,
                now.AddSeconds(PracticeMapChangeUtility.PendingSeconds + 1)
            )
        );
        Assert.True(PracticeMapChangeUtility.IsExpired(null, now));
    }
}
