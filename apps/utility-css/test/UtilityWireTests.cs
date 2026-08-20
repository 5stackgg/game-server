using System.Text.Json;
using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using Xunit;

// The API owns the wire contract, so these pin the translation to it. Every
// case here is one that fails silently rather than loudly: a field that lands
// in the wrong column, a unit that is a thousand times out, or a spelling the
// panel's enum does not have.
public class UtilityWireTests
{
    private static LineupRecord Lineup()
    {
        return new LineupRecord
        {
            id = "server-side-id",
            client_id = "local-id",
            map = "de_mirage",
            name = "A site window smoke",
            utility_type = "Smoke",
            side = "TERRORIST",
            visibility = "Private",
            author_steam_id = "76561198000000001",
            release = new ThrowSnapshot
            {
                feet_position = new Vec3(100f, 200f, 300f),
                eye_position = new Vec3(100f, 200f, 364f),
                pitch = -12.5f,
                yaw = 90f,
                jump_throw = true,
            },
            initial_position = new Vec3(1f, 2f, 3f),
            initial_velocity = new Vec3(4f, 5f, 6f),
            detonation_position = new Vec3(-500f, -600f, 128f),
            bounces = 2,
            flight_time = 1.5f,
            technique = "RunJump",
            strength = "Full",
            recorded_tickrate = 64,
            confidence = LineupRecord.Exact,
            plugin_runtime = "counterstrikesharp",
            plugin_version = "0.0.1",
            trajectory = new List<TrajectoryPoint>
            {
                new TrajectoryPoint { p = new Vec3(1f, 2f, 3f), t = 10 },
                new TrajectoryPoint { p = new Vec3(4f, 5f, 6f), t = 12, bounce = true },
            },
        };
    }

    [Fact]
    public void EveryFieldLandsInTheColumnTheApiNames()
    {
        UtilityIngestPayload payload = UtilityIngestPayload.From(Lineup());

        Assert.Equal("76561198000000001", payload.author_steam_id);
        Assert.Equal("Smoke", payload.utility_type);
        Assert.Equal("TERRORIST", payload.side);
        Assert.Equal("RunJump", payload.technique);
        Assert.Equal("Full", payload.throw_strength);
        Assert.True(payload.jump_throw_bind);

        Assert.Equal(100f, payload.origin_x);
        Assert.Equal(200f, payload.origin_y);
        Assert.Equal(300f, payload.origin_z);
        Assert.Equal(364f, payload.eye_z);

        Assert.Equal(90f, payload.view_yaw);
        Assert.Equal(-12.5f, payload.view_pitch);

        Assert.Equal(-500f, payload.land_x);
        Assert.Equal(-600f, payload.land_y);
        Assert.Equal(128f, payload.land_z);

        Assert.Equal("A site window smoke", payload.name);
        Assert.Equal(64, payload.tick_rate);
    }

    // Seconds on this side, milliseconds on the API's. Getting this wrong
    // produces a plausible-looking number rather than an error.
    [Fact]
    public void FlightTimeCrossesTheWireInMilliseconds()
    {
        UtilityIngestPayload payload = UtilityIngestPayload.From(Lineup());

        Assert.Equal(1500, payload.flight_time_ms);
    }

    [Theory]
    [InlineData(0f, 0)]
    [InlineData(0.001f, 1)]
    [InlineData(2.4f, 2400)]
    [InlineData(20f, 20000)]
    public void MillisecondsAreRoundedNotTruncated(float seconds, int expected)
    {
        Assert.Equal(expected, UtilityIngestPayload.MillisecondsFromSeconds(seconds));
    }

    // 62.5 is exactly representable, so this pins the midpoint rule rather
    // than tolerating whatever the default happens to be.
    [Fact]
    public void AMidpointRoundsAwayFromZero()
    {
        Assert.Equal(63, UtilityIngestPayload.MillisecondsFromSeconds(0.0625f));
    }

    [Theory]
    [InlineData("HE", "HighExplosive")]
    [InlineData("HEGrenade", "HighExplosive")]
    [InlineData("HighExplosive", "HighExplosive")]
    [InlineData("Flashbang", "Flash")]
    [InlineData("Incendiary", "Molotov")]
    [InlineData("Smoke", "Smoke")]
    [InlineData("Decoy", "Decoy")]
    public void TheUtilityTypeIsSentInTheApisSpelling(string recorded, string expected)
    {
        LineupRecord lineup = Lineup();
        lineup.utility_type = recorded;

        Assert.Equal(expected, UtilityIngestPayload.From(lineup).utility_type);
    }

    [Fact]
    public void ThePathIsSentAsObjectsNotPackedArrays()
    {
        string json = JsonSerializer.Serialize(
            UtilityIngestPayload.From(Lineup()),
            PracticeJson.Options
        );

        Assert.Contains("\"path\":[{\"tick\":10,\"x\":1,\"y\":2,\"z\":3}", json);
        Assert.DoesNotContain("[[", json);
    }

    // Sending a field the payload does not name is not harmless: the API
    // derives the map from the server's own match row and rejects a mismatch.
    [Theory]
    [InlineData("\"map\"")]
    [InlineData("\"client_id\"")]
    [InlineData("\"initial_position\"")]
    [InlineData("\"initial_velocity\"")]
    [InlineData("\"bounces\"")]
    [InlineData("\"visibility\"")]
    [InlineData("\"plugin_runtime\"")]
    [InlineData("\"plugin_version\"")]
    [InlineData("\"workshop_map_id\"")]
    [InlineData("\"release\"")]
    [InlineData("\"trajectory\"")]
    [InlineData("\"flight_time\":")]
    [InlineData("\"confidence\"")]
    public void FieldsTheApiDoesNotAcceptAreNotSent(string absent)
    {
        string json = JsonSerializer.Serialize(
            UtilityIngestPayload.From(Lineup()),
            PracticeJson.Options
        );

        Assert.DoesNotContain(absent, json);
    }

    private static UtilityLibraryRow Row()
    {
        return new UtilityLibraryRow
        {
            id = "panel-id",
            name = "Window smoke",
            map_name = "de_mirage",
            utility_type = "HE",
            side = "CT",
            technique = "Jump",
            throw_strength = "Half",
            jump_throw_bind = true,
            origin_x = 10f,
            origin_y = 20f,
            origin_z = 30f,
            eye_z = 94f,
            view_yaw = 45f,
            view_pitch = -20f,
            land_x = 700f,
            land_y = 800f,
            land_z = 90f,
            flight_time_ms = 2400,
            visibility = "Team",
            author_steam_id = "76561198000000009",
        };
    }

    [Fact]
    public void ALibraryRowBecomesALineupTheReplayCanStandOn()
    {
        LineupRecord lineup = Row().ToLineup();

        Assert.Equal("panel-id", lineup.id);
        Assert.Equal("Window smoke", lineup.name);
        Assert.Equal("de_mirage", lineup.map);
        Assert.Equal("CT", lineup.side);
        Assert.Equal("Jump", lineup.technique);
        Assert.Equal("Half", lineup.strength);
        Assert.Equal("Team", lineup.visibility);
        Assert.Equal("76561198000000009", lineup.author_steam_id);

        Assert.Equal(10f, lineup.release.feet_position.x);
        Assert.Equal(20f, lineup.release.feet_position.y);
        Assert.Equal(30f, lineup.release.feet_position.z);
        Assert.Equal(94f, lineup.release.eye_position.z);
        Assert.Equal(45f, lineup.release.yaw);
        Assert.Equal(-20f, lineup.release.pitch);
        Assert.True(lineup.release.jump_throw);

        Assert.Equal(700f, lineup.detonation_position.x);
        Assert.Equal(90f, lineup.detonation_position.z);
    }

    [Fact]
    public void MillisecondsComeBackAsSeconds()
    {
        Assert.Equal(2.4f, Row().ToLineup().flight_time);
    }

    [Fact]
    public void ARowsTypeIsNormalizedOnTheWayInToo()
    {
        Assert.Equal("HighExplosive", Row().ToLineup().utility_type);
    }

    // .delete and the .next/.prev walk both key off client_id, so a fetched
    // lineup has to keep a stable one across reloads.
    [Fact]
    public void ThePanelsIdBecomesTheLocalIdentity()
    {
        Assert.Equal("panel-id", Row().ToLineup().client_id);
    }

    // The library response has no path in it at all; the preview needs a
    // second call, and code that assumed otherwise would draw nothing.
    [Fact]
    public void ALibraryRowCarriesNoTrajectory()
    {
        Assert.Empty(Row().ToLineup().trajectory);
    }

    // The seed columns are nullable by design: a lineup mined from a demo,
    // imported or authored by hand was never watched by a plugin. A zero
    // velocity is exactly the predicate PracticeReplay.HasPhysicsSeed reads as
    // "do not re-emit this", so a row with no seed has to land on it.
    [Fact]
    public void ARowWithNoSeedIsNotReplayable()
    {
        LineupRecord lineup = Row().ToLineup();

        Assert.False(Row().HasSeed());
        Assert.Equal(0f, lineup.initial_velocity.Length());
        Assert.Equal(0f, lineup.initial_position.Length());
    }

    [Fact]
    public void ARowWithASeedIsReplayableExactly()
    {
        UtilityLibraryRow row = Seeded();
        LineupRecord lineup = row.ToLineup();

        Assert.True(row.HasSeed());

        Assert.Equal(11f, lineup.initial_position.x);
        Assert.Equal(22f, lineup.initial_position.y);
        Assert.Equal(33f, lineup.initial_position.z);

        Assert.Equal(400f, lineup.initial_velocity.x);
        Assert.Equal(-500f, lineup.initial_velocity.y);
        Assert.Equal(600f, lineup.initial_velocity.z);

        Assert.True(lineup.initial_velocity.Length() > 0f);
    }

    // Half a seed is worse than none: a position without a velocity would put
    // the replay's origin somewhere real and its aim at nothing.
    [Theory]
    [InlineData("initial_pos_z")]
    [InlineData("initial_vel_x")]
    [InlineData("initial_vel_y")]
    [InlineData("initial_vel_z")]
    public void HalfASeedIsNoSeed(string missing)
    {
        UtilityLibraryRow row = Seeded();

        switch (missing)
        {
            case "initial_pos_z":
                row.initial_pos_z = null;
                break;
            case "initial_vel_x":
                row.initial_vel_x = null;
                break;
            case "initial_vel_y":
                row.initial_vel_y = null;
                break;
            default:
                row.initial_vel_z = null;
                break;
        }

        LineupRecord lineup = row.ToLineup();

        Assert.False(row.HasSeed());
        Assert.Equal(0f, lineup.initial_velocity.Length());
        Assert.Equal(0f, lineup.initial_position.Length());
    }

    // A grenade never leaves the hand at rest, so all six columns present and
    // the velocity zero is an unfilled row rather than a throw. Taking it would
    // fire the replay out of the world origin.
    [Fact]
    public void AZeroedSeedIsNoSeed()
    {
        UtilityLibraryRow row = Seeded();
        row.initial_vel_x = 0f;
        row.initial_vel_y = 0f;
        row.initial_vel_z = 0f;

        LineupRecord lineup = row.ToLineup();

        Assert.False(row.HasSeed());
        Assert.Equal(0f, lineup.initial_velocity.Length());
        Assert.Equal(0f, lineup.initial_position.Length());
    }

    // An oracle solver is only worth running if the seed it found comes back
    // out of the panel able to reproduce the throw it found.
    [Fact]
    public void ASeedSurvivesTheRoundTripThroughBothShapes()
    {
        LineupRecord original = Lineup();
        UtilityLibraryRow row = Seeded();

        row.initial_pos_x = original.initial_position.x;
        row.initial_pos_y = original.initial_position.y;
        row.initial_pos_z = original.initial_position.z;
        row.initial_vel_x = original.initial_velocity.x;
        row.initial_vel_y = original.initial_velocity.y;
        row.initial_vel_z = original.initial_velocity.z;

        LineupRecord back = row.ToLineup();

        Assert.Equal(original.initial_position.x, back.initial_position.x);
        Assert.Equal(original.initial_position.y, back.initial_position.y);
        Assert.Equal(original.initial_position.z, back.initial_position.z);
        Assert.Equal(original.initial_velocity.x, back.initial_velocity.x);
        Assert.Equal(original.initial_velocity.y, back.initial_velocity.y);
        Assert.Equal(original.initial_velocity.z, back.initial_velocity.z);
    }

    // A seed and an exact lineup are the same signal today and not the same
    // statement: the panel stamps a plugin-recorded lineup "exact" whether or
    // not it captured a seed, and a mined lineup that later acquires one is
    // still a path fitted to a demo. Re-emit needs both.
    [Fact]
    public void ExactWithASeedIsExactlyReplayable()
    {
        LineupRecord lineup = Seeded("exact").ToLineup();

        Assert.True(lineup.HasPhysicsSeed());
        Assert.True(lineup.IsExactlyReplayable());
        Assert.False(lineup.IsKnownInexact());
    }

    [Theory]
    [InlineData("derived")]
    [InlineData("low")]
    public void ASeedOnAnInexactLineupIsNotReplayed(string confidence)
    {
        LineupRecord lineup = Seeded(confidence).ToLineup();

        Assert.Equal(confidence, lineup.confidence);
        Assert.True(lineup.HasPhysicsSeed());
        Assert.False(lineup.IsExactlyReplayable());
        Assert.True(lineup.IsKnownInexact());
    }

    // An older panel does not send the field at all. Defaulting that to exact
    // would put the bug back on the deployments least able to spot it.
    [Fact]
    public void AMissingConfidenceIsNotExact()
    {
        LineupRecord lineup = Seeded(null).ToLineup();

        Assert.Null(lineup.confidence);
        Assert.True(lineup.HasPhysicsSeed());
        Assert.False(lineup.IsExactlyReplayable());
    }

    // Unknown is not the same as bad. Warning about every lineup an older panel
    // returns would teach a player to ignore the warning that matters.
    [Fact]
    public void AMissingConfidenceIsNotWarnedAbout()
    {
        Assert.False(Seeded(null).ToLineup().IsKnownInexact());
        Assert.False(Row().ToLineup().IsKnownInexact());
    }

    [Fact]
    public void ExactWithNoSeedIsStillNotReplayable()
    {
        UtilityLibraryRow row = Row();
        row.confidence = "exact";

        LineupRecord lineup = row.ToLineup();

        Assert.False(lineup.HasPhysicsSeed());
        Assert.False(lineup.IsExactlyReplayable());
    }

    [Fact]
    public void ConfidenceIsMatchedWhateverItsCasing()
    {
        Assert.True(Seeded("Exact").ToLineup().IsExactlyReplayable());
        Assert.True(Seeded("EXACT").ToLineup().IsExactlyReplayable());
    }

    // A lineup recorded in this session was watched by the plugin, so
    // PracticeRecorder stamps it exact as it finalizes -- without that stamp
    // the gate below would refuse to replay the one kind of throw the plugin
    // measured itself.
    [Fact]
    public void ALineupRecordedHereIsExactlyReplayable()
    {
        LineupRecord recorded = Lineup();

        Assert.Equal(LineupRecord.Exact, recorded.confidence);
        Assert.True(recorded.IsExactlyReplayable());
    }

    private static UtilityLibraryRow Seeded(string? confidence = null)
    {
        UtilityLibraryRow row = Row();

        row.initial_pos_x = 11f;
        row.initial_pos_y = 22f;
        row.initial_pos_z = 33f;
        row.initial_vel_x = 400f;
        row.initial_vel_y = -500f;
        row.initial_vel_z = 600f;
        row.confidence = confidence;

        return row;
    }

    [Fact]
    public void APositionSurvivesTheRoundTripThroughBothShapes()
    {
        LineupRecord original = Lineup();
        UtilityIngestPayload payload = UtilityIngestPayload.From(original);

        var row = new UtilityLibraryRow
        {
            id = "panel-id",
            name = payload.name,
            utility_type = payload.utility_type,
            side = payload.side,
            technique = payload.technique,
            throw_strength = payload.throw_strength,
            jump_throw_bind = payload.jump_throw_bind,
            origin_x = payload.origin_x,
            origin_y = payload.origin_y,
            origin_z = payload.origin_z,
            eye_z = payload.eye_z,
            view_yaw = payload.view_yaw,
            view_pitch = payload.view_pitch,
            land_x = payload.land_x,
            land_y = payload.land_y,
            land_z = payload.land_z,
            flight_time_ms = payload.flight_time_ms,
        };

        LineupRecord back = row.ToLineup();

        Assert.Equal(original.release.feet_position.x, back.release.feet_position.x);
        Assert.Equal(original.release.feet_position.z, back.release.feet_position.z);
        Assert.Equal(original.release.eye_position.z, back.release.eye_position.z);
        Assert.Equal(original.release.yaw, back.release.yaw);
        Assert.Equal(original.release.pitch, back.release.pitch);
        Assert.Equal(original.detonation_position.y, back.detonation_position.y);
        Assert.Equal(original.flight_time, back.flight_time);
        Assert.Equal(original.utility_type, back.utility_type);
        Assert.Equal(original.technique, back.technique);
    }

    [Fact]
    public void NullsAreOmittedRatherThanSentAsNull()
    {
        var bare = new LineupRecord { utility_type = "Smoke" };

        string json = JsonSerializer.Serialize(
            UtilityIngestPayload.From(bare),
            PracticeJson.Options
        );

        Assert.DoesNotContain("null", json);
        Assert.DoesNotContain("\"description\"", json);
        Assert.DoesNotContain("\"match_id\"", json);
    }
}
