using System.IO.Compression;
using System.Text.Json;
using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using Xunit;

// The three shapes the panel added, pinned to the spellings it actually sends.
// Every failure here is silent rather than loud: a preview that draws nothing,
// a roster that reads as empty, or a result the panel answers 403 to.
public class NadeArtifactTests
{
    // The artifact is the playback blob's shape, so the path is nested.
    private const string Artifact = """
        {
          "schema_version": 3,
          "map_name": "de_mirage",
          "grenade_trajectories": [
            {
              "round": 1,
              "grenade_id": 1,
              "type": "Smoke",
              "points": [
                { "tick": 0, "x": 1, "y": 2, "z": 3 },
                { "tick": 8, "x": 4.5, "y": 5.5, "z": 6.5 }
              ]
            }
          ],
          "smoke_volumes": [
            {
              "gid": 1,
              "round": 1,
              "start_tick": 128,
              "ox": -100, "oy": -200, "oz": 64,
              "vs": 8, "dx": 4, "dy": 5, "dz": 6,
              "den": "AAAA"
            }
          ]
        }
        """;

    [Fact]
    public void ThePathIsReadOutOfTheNestedTrajectory()
    {
        NadeTrajectoryArtifact artifact = NadeTrajectoryArtifact.Parse(Artifact);

        Assert.Equal(2, artifact.path.Count);
        Assert.Equal(0, artifact.path[0].t);
        Assert.Equal(1f, artifact.path[0].p.x);
        Assert.Equal(8, artifact.path[1].t);
        Assert.Equal(6.5f, artifact.path[1].p.z);
    }

    [Fact]
    public void TheSmokeVolumeIsReadOutOfTheArtifact()
    {
        SmokeVolume? volume = NadeTrajectoryArtifact.Parse(Artifact).smoke_volume;

        Assert.NotNull(volume);
        Assert.Equal(-100f, volume!.ox);
        Assert.Equal(64f, volume.oz);
        Assert.Equal(8f, volume.vs);
        Assert.Equal(4, volume.dx);
        Assert.Equal(5, volume.dy);
        Assert.Equal(6, volume.dz);
        Assert.Equal("AAAA", volume.den);
    }

    // The blob is stored gzipped and streamed back byte for byte, so what
    // arrives is compressed and reading it as text would be mojibake.
    [Fact]
    public void AGzippedArtifactIsUnpackedFirst()
    {
        var plain = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(Artifact));
        var compressed = new MemoryStream();

        using (var gzip = new GZipStream(compressed, CompressionMode.Compress, true))
        {
            plain.CopyTo(gzip);
        }

        NadeTrajectoryArtifact artifact = NadeTrajectoryArtifact.Parse(compressed.ToArray());

        Assert.Equal(2, artifact.path.Count);
        Assert.NotNull(artifact.smoke_volume);
    }

    [Fact]
    public void APlainBodyIsReadAsItIs()
    {
        Assert.Equal("{}", PracticeJson.Text(System.Text.Encoding.UTF8.GetBytes("{}")));
    }

    [Fact]
    public void AFlatPathIsStillRead()
    {
        NadeTrajectoryArtifact artifact = NadeTrajectoryArtifact.Parse(
            """{"path":[{"tick":4,"x":1,"y":2,"z":3}]}"""
        );

        Assert.Single(artifact.path);
        Assert.Equal(4, artifact.path[0].t);
    }

    [Fact]
    public void ABareArrayIsStillRead()
    {
        NadeTrajectoryArtifact artifact = NadeTrajectoryArtifact.Parse(
            """[{"tick":4,"x":1,"y":2,"z":3}]"""
        );

        Assert.Single(artifact.path);
    }

    // Not every lineup is a smoke and not every map has a collision mesh.
    [Fact]
    public void AMissingSmokeVolumeIsNotAnError()
    {
        NadeTrajectoryArtifact artifact = NadeTrajectoryArtifact.Parse(
            """{"grenade_trajectories":[{"points":[]}],"smoke_volumes":[]}"""
        );

        Assert.Empty(artifact.path);
        Assert.Null(artifact.smoke_volume);
    }

    [Fact]
    public void AVolumeWithNoExtentIsNotAVolume()
    {
        NadeTrajectoryArtifact artifact = NadeTrajectoryArtifact.Parse(
            """{"smoke_volumes":[{"ox":0,"oy":0,"oz":0,"vs":0,"dx":0,"dy":0,"dz":0}]}"""
        );

        Assert.Null(artifact.smoke_volume);
    }

    [Fact]
    public void AnArtifactWithNothingInItReadsAsEmpty()
    {
        NadeTrajectoryArtifact artifact = NadeTrajectoryArtifact.Parse("{}");

        Assert.Empty(artifact.path);
        Assert.Null(artifact.smoke_volume);
    }

    [Fact]
    public void TheSessionIsReadInTheApiSpelling()
    {
        PracticeSessionData session = JsonSerializer
            .Deserialize<NadeSessionRow>(
                """
                {
                  "session_id": "11111111-1111-1111-1111-111111111111",
                  "match_id": "22222222-2222-2222-2222-222222222222",
                  "map_name": "de_nuke",
                  "password": "hunter2",
                  "steam_ids": ["76561198000000001", "76561198000000002"],
                  "playbook": null
                }
                """,
                PracticeJson.Options
            )!
            .ToSession();

        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), session.id);
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), session.match_id);
        Assert.Equal("de_nuke", session.map);
        Assert.Equal("hunter2", session.password);
        Assert.Equal(2, session.allowed_steam_ids.Count);
        Assert.Null(session.playbook);
    }

    // An unparsed roster reads as "nobody is allowed", which is why both
    // spellings are accepted rather than the newest one only.
    [Fact]
    public void TheOlderSessionSpellingStillFillsTheRoster()
    {
        PracticeSessionData session = JsonSerializer
            .Deserialize<NadeSessionRow>(
                """
                {
                  "id": "11111111-1111-1111-1111-111111111111",
                  "match_id": "22222222-2222-2222-2222-222222222222",
                  "map": "de_nuke",
                  "password": "hunter2",
                  "allowed_steam_ids": ["76561198000000001"]
                }
                """,
                PracticeJson.Options
            )!
            .ToSession();

        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), session.id);
        Assert.Equal("de_nuke", session.map);
        Assert.Single(session.allowed_steam_ids);
    }

    [Fact]
    public void APlaybookOnTheSessionArrivesWithItsSteps()
    {
        PracticeSessionData session = JsonSerializer
            .Deserialize<NadeSessionRow>(
                """
                {
                  "session_id": "11111111-1111-1111-1111-111111111111",
                  "match_id": "22222222-2222-2222-2222-222222222222",
                  "map_name": "de_mirage",
                  "password": "",
                  "steam_ids": [],
                  "playbook": {
                    "id": "33333333-3333-3333-3333-333333333333",
                    "name": "A split",
                    "map_name": "de_mirage",
                    "side": "TERRORIST",
                    "steps": [
                      {
                        "nade_lineup_id": "44444444-4444-4444-4444-444444444444",
                        "step_order": 1,
                        "offset_ms": 0,
                        "assigned_steam_id": "76561198000000001",
                        "note": "jungle smoke",
                        "lineup": {
                          "id": "44444444-4444-4444-4444-444444444444",
                          "name": "jungle",
                          "map_name": "de_mirage",
                          "nade_type": "Smoke",
                          "side": "TERRORIST",
                          "origin_x": 1, "origin_y": 2, "origin_z": 3,
                          "view_yaw": 90, "view_pitch": -20,
                          "land_x": 400, "land_y": 500, "land_z": 60
                        }
                      }
                    ]
                  }
                }
                """,
                PracticeJson.Options
            )!
            .ToSession();

        NadePlaybook? playbook = session.playbook;

        Assert.NotNull(playbook);
        Assert.Equal("A split", playbook!.name);

        var steps = PlaybookUtility.Ordered(playbook);

        Assert.Single(steps);
        Assert.Equal("jungle smoke", steps[0].note);
        Assert.True(PlaybookUtility.IsFor(steps[0], 76561198000000001));

        LineupRecord? lineup = steps[0].ToLineup();

        Assert.NotNull(lineup);
        Assert.Equal("44444444-4444-4444-4444-444444444444", lineup!.id);
        Assert.Equal(400f, lineup.detonation_position.x);
    }

    [Fact]
    public void AResultNamesTheServerAndTheSessionItBelongsTo()
    {
        NadePracticeResultPayload payload = NadePracticeResultPayload.For(
            "55555555-5555-5555-5555-555555555555",
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "44444444-4444-4444-4444-444444444444",
            76561198000000001,
            new Vec3(1f, 2f, 3f),
            true
        );

        Assert.Equal("55555555-5555-5555-5555-555555555555", payload.server_id);
        Assert.Equal("11111111-1111-1111-1111-111111111111", payload.session_id);
        Assert.Equal("44444444-4444-4444-4444-444444444444", payload.nade_lineup_id);
        Assert.Equal("76561198000000001", payload.steam_id);
        Assert.Equal(3f, payload.land_z);
        Assert.True(payload.success);
    }

    // The API rejects a session_id that disagrees with the one it resolved from
    // the server, so an unknown session must be left out rather than sent empty.
    [Fact]
    public void AnUnknownSessionIsLeftOutOfTheResult()
    {
        NadePracticeResultPayload payload = NadePracticeResultPayload.For(
            null,
            Guid.Empty,
            "44444444-4444-4444-4444-444444444444",
            76561198000000001,
            new Vec3(1f, 2f, 3f),
            null
        );

        string json = JsonSerializer.Serialize(payload, PracticeJson.Options);

        Assert.DoesNotContain("session_id", json);
        Assert.DoesNotContain("server_id", json);
        Assert.DoesNotContain("success", json);
        Assert.Contains("\"nade_lineup_id\"", json);
    }

    [Fact]
    public void AResultIsReadBackWithThePanelsRadius()
    {
        NadePracticeResult? result = JsonSerializer.Deserialize<NadePracticeResult>(
            """
            {
              "success": true,
              "distance": 42.5,
              "radius": 96,
              "attempts": 7,
              "successes": 4,
              "current_streak": 3,
              "best_streak": 5,
              "mastered_at": "2026-08-18T12:00:00.000Z"
            }
            """,
            PracticeJson.Options
        );

        Assert.NotNull(result);
        Assert.True(result!.success);
        Assert.Equal(42.5f, result.distance);
        Assert.Equal(96f, result.radius);
        Assert.Equal(3, result.current_streak);
        Assert.NotNull(result.mastered_at);
    }

    [Fact]
    public void AResultThatHasNotBeenMasteredCarriesNoDate()
    {
        NadePracticeResult? result = JsonSerializer.Deserialize<NadePracticeResult>(
            """{"success":false,"distance":300,"radius":96,"attempts":1,"successes":0,"current_streak":0,"best_streak":0,"mastered_at":null}""",
            PracticeJson.Options
        );

        Assert.NotNull(result);
        Assert.Null(result!.mastered_at);
    }
}
