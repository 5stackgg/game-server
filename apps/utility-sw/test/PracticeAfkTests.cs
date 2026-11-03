using System.Text.Json;
using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using Xunit;

public class PracticeAfkTests
{
    private const ulong Idle = 76561198000000001;
    private const ulong Busy = 76561198000000002;

    private static PracticeAfk.Sample Still(ulong steamId)
    {
        return new PracticeAfk.Sample(steamId, true, true, false, 0, (90f, 10f), (100f, 200f, 0f));
    }

    private static PracticeAfk.Sample Pressing(ulong steamId)
    {
        return Still(steamId) with { Stirred = true };
    }

    private static PracticeAfk.Sample Spectating(ulong steamId)
    {
        return Still(steamId) with { Alive = false };
    }

    private static List<(ulong SteamId, PracticeAfk.Step Step, int At)> Run(
        PracticeAfk afk,
        int from,
        int to,
        int minutes,
        Func<int, IEnumerable<PracticeAfk.Sample>> players
    )
    {
        var due = new List<(ulong, PracticeAfk.Step, int)>();

        for (int now = from; now <= to; now++)
        {
            foreach (PracticeAfk.Due step in afk.Sweep(players(now), now, minutes))
            {
                due.Add((step.SteamId, step.Step, now));
            }
        }

        return due;
    }

    private static IEnumerable<(PracticeAfk.Step, int)> Steps(
        List<(ulong SteamId, PracticeAfk.Step Step, int At)> due
    )
    {
        return due.Select(step => (step.Step, step.At));
    }

    [Fact]
    public void WarnsThenWarnsAgainThenKicksAnIdlePlayer()
    {
        var afk = new PracticeAfk();

        var due = Run(afk, 0, 3600, 15, _ => new[] { Still(Idle) });

        Assert.Equal(
            new[]
            {
                (PracticeAfk.Step.Warn, 600),
                (PracticeAfk.Step.FinalWarn, 840),
                (PracticeAfk.Step.Kick, 900),
            },
            Steps(due)
        );
    }

    [Fact]
    public void TheWarningsSayHowLongIsLeft()
    {
        var afk = new PracticeAfk();
        var said = new List<PracticeAfk.Due>();

        for (int now = 0; now <= 900; now++)
        {
            said.AddRange(afk.Sweep(new[] { Still(Idle) }, now, 15));
        }

        Assert.Equal(300, said[0].SecondsLeft);
        Assert.Equal(60, said[1].SecondsLeft);
        Assert.Contains("in 5 minutes", PracticeAfk.Warning(said[0].Step, said[0].SecondsLeft));
        Assert.Contains("in 1 minute.", PracticeAfk.Warning(said[1].Step, said[1].SecondsLeft));
        Assert.StartsWith("Final warning", PracticeAfk.Warning(said[1].Step, said[1].SecondsLeft));
        Assert.Contains("30 seconds", PracticeAfk.Warning(PracticeAfk.Step.Warn, 30));
        Assert.Contains("15 minutes", PracticeAfk.KickReason(900));
    }

    [Fact]
    public void PressingAnythingStartsTheClockOver()
    {
        var afk = new PracticeAfk();

        var due = Run(
            afk,
            0,
            1500,
            15,
            now => new[] { now == 700 ? Pressing(Idle) : Still(Idle) }
        );

        Assert.Equal(
            new[] { (PracticeAfk.Step.Warn, 600), (PracticeAfk.Step.Warn, 1300) },
            Steps(due)
        );
    }

    [Fact]
    public void LookingAroundCountsButAimJitterDoesNot()
    {
        var jittering = new PracticeAfk();

        var jitter = Run(
            jittering,
            0,
            900,
            15,
            now => new[] { Still(Idle) with { View = (90f + (now % 2) * 0.5f, 10f) } }
        );

        Assert.Contains(jitter, step => step.Step == PracticeAfk.Step.Kick);

        var looking = new PracticeAfk();

        var aimed = Run(
            looking,
            0,
            3600,
            15,
            now => new[] { Still(Idle) with { View = (90f + (now / 300) * 2f, 10f) } }
        );

        Assert.Empty(aimed);
    }

    [Fact]
    public void LookingAcrossTheSeamCounts()
    {
        Assert.True(PracticeAfk.Looked((359.5f, 0f), (1.5f, 0f)));
        Assert.False(PracticeAfk.Looked((359.8f, 0f), (0.2f, 0f)));
        Assert.True(PracticeAfk.Looked((10f, 0f), (10f, 1.5f)));
        Assert.False(PracticeAfk.Looked(null, (10f, 0f)));
    }

    [Fact]
    public void WalkingCountsButSettlingDoesNot()
    {
        Assert.True(PracticeAfk.Moved((0f, 0f, 0f), (5f, 0f, 0f)));
        Assert.False(PracticeAfk.Moved((0f, 0f, 0f), (2f, 2f, 0f)));
        Assert.False(PracticeAfk.Moved((0f, 0f, 0f), null));

        var afk = new PracticeAfk();

        var walking = Run(
            afk,
            0,
            3600,
            15,
            now => new[] { Still(Idle) with { At = (100f + (now / 300) * 10f, 200f, 0f) } }
        );

        Assert.Empty(walking);
    }

    [Fact]
    public void ZeroMinutesIsOff()
    {
        var afk = new PracticeAfk();

        Assert.Empty(Run(afk, 0, 7200, 0, _ => new[] { Still(Idle) }));
    }

    [Fact]
    public void APlayerStillLoadingIsNotOnTheClock()
    {
        var afk = new PracticeAfk();

        var due = Run(
            afk,
            0,
            1200,
            15,
            now => new[] { Still(Idle) with { InGame = now >= 500 } }
        );

        Assert.Equal((PracticeAfk.Step.Warn, 1099), Steps(due).First());
    }

    [Fact]
    public void EveryPlayerHasTheirOwnClock()
    {
        var afk = new PracticeAfk();

        var due = Run(
            afk,
            0,
            1800,
            15,
            now => new[] { Still(Idle), now % 60 == 0 ? Pressing(Busy) : Still(Busy) }
        );

        Assert.All(due, step => Assert.Equal(Idle, step.SteamId));
        Assert.Contains(due, step => step.Step == PracticeAfk.Step.Kick);
    }

    [Fact]
    public void APlayerSeenForTheFirstTimeIsNotJudgedOnTimeNobodyWatched()
    {
        var afk = new PracticeAfk();

        Assert.Empty(afk.Sweep(new[] { Still(Idle) }, 100_000, 15));
        Assert.Empty(afk.Sweep(new[] { Still(Idle) }, 100_001, 15));
    }

    [Fact]
    public void ALeaverComesBackToAFreshClock()
    {
        var afk = new PracticeAfk();

        Run(afk, 0, 850, 15, _ => new[] { Still(Idle) });
        afk.Sweep(Array.Empty<PracticeAfk.Sample>(), 851, 15);

        var back = Run(afk, 852, 1500, 15, _ => new[] { Still(Idle) });

        Assert.Equal(new[] { (PracticeAfk.Step.Warn, 1452) }, Steps(back));
    }

    [Fact]
    public void ForgettingEveryoneStartsEveryClockOver()
    {
        var afk = new PracticeAfk();

        Run(afk, 0, 800, 15, _ => new[] { Still(Idle) });
        afk.Reset();

        var due = Run(afk, 801, 1500, 15, _ => new[] { Still(Idle) });

        Assert.Equal((PracticeAfk.Step.Warn, 1401), Steps(due).First());
    }

    [Fact]
    public void TurningItOnJudgesNobodyOnTheTimeItWasOff()
    {
        var afk = new PracticeAfk();

        Run(afk, 0, 1000, 0, _ => new[] { Still(Idle) });

        var due = Run(afk, 1001, 2000, 1, _ => new[] { Still(Idle) });

        Assert.Equal(
            new[]
            {
                (PracticeAfk.Step.Warn, 1031),
                (PracticeAfk.Step.FinalWarn, 1046),
                (PracticeAfk.Step.Kick, 1061),
            },
            Steps(due)
        );
    }

    // A shorter limit still warns first, for that limit's full lead.
    [Fact]
    public void ShorteningTheLimitStillWarnsBeforeTheKick()
    {
        var afk = new PracticeAfk();

        Assert.Empty(Run(afk, 0, 800, 60, _ => new[] { Still(Idle) }));

        var due = Run(afk, 801, 2000, 1, _ => new[] { Still(Idle) });

        Assert.Equal(
            new[]
            {
                (PracticeAfk.Step.Warn, 801),
                (PracticeAfk.Step.FinalWarn, 816),
                (PracticeAfk.Step.Kick, 831),
            },
            Steps(due)
        );
    }

    // A warning is a promise of how long is left, so a change of setting after
    // it was given never brings the kick forward.
    [Fact]
    public void AWarningAlreadyGivenKeepsItsKickTime()
    {
        var afk = new PracticeAfk();

        Run(afk, 0, 700, 15, _ => new[] { Still(Idle) });

        var due = Run(afk, 701, 2000, 1, _ => new[] { Still(Idle) });

        Assert.Equal(
            new[] { (PracticeAfk.Step.FinalWarn, 885), (PracticeAfk.Step.Kick, 900) },
            Steps(due)
        );
    }

    [Theory]
    [InlineData(1, 30, 15)]
    [InlineData(3, 60, 30)]
    [InlineData(5, 120, 60)]
    [InlineData(10, 300, 60)]
    [InlineData(15, 300, 60)]
    [InlineData(60, 300, 60)]
    public void TheLeadsFitTheSetting(int minutes, int warn, int final)
    {
        Assert.Equal((warn, final), PracticeAfk.Leads(minutes * 60));
    }

    [Fact]
    public void HoldingAKeyCountsForAPlayerWhoIsAlive()
    {
        var afk = new PracticeAfk();

        Assert.Empty(Run(afk, 0, 3600, 15, _ => new[] { Still(Idle) with { Buttons = 8 } }));
    }

    // A pawn that died with a key down keeps reporting it.
    [Fact]
    public void ADeadPawnsFrozenButtonsDoNotCount()
    {
        var afk = new PracticeAfk();

        var due = Run(afk, 0, 900, 15, _ => new[] { Spectating(Idle) with { Buttons = 8 } });

        Assert.Contains(due, step => step.Step == PracticeAfk.Step.Kick);
    }

    [Fact]
    public void ASpectatorLookingAroundInFreeRoamCounts()
    {
        var afk = new PracticeAfk();

        var due = Run(
            afk,
            0,
            3600,
            15,
            now => new[]
            {
                Spectating(Idle) with { Roaming = true, View = (90f + (now / 300) * 2f, 10f) },
            }
        );

        Assert.Empty(due);
    }

    // In eye or chase, the observer's view and seat follow whoever they
    // watch: that is the target moving, not the spectator.
    [Fact]
    public void ASpectatorFollowingSomeoneIsNotStirredByThem()
    {
        var afk = new PracticeAfk();

        var due = Run(
            afk,
            0,
            900,
            15,
            now => new[]
            {
                Spectating(Idle) with
                {
                    View = (90f + (now % 7) * 20f, 10f),
                    At = (100f + now, 200f, 0f),
                },
            }
        );

        Assert.Contains(due, step => step.Step == PracticeAfk.Step.Kick);
    }

    [Fact]
    public void ASpectatorTalkingOrTypingCounts()
    {
        var afk = new PracticeAfk();

        var due = Run(
            afk,
            0,
            3600,
            15,
            now => new[] { Spectating(Idle) with { Stirred = now % 300 == 0 } }
        );

        Assert.Empty(due);
    }

    // Raising the limit is a reprieve: the warning given under the old one no
    // longer stands.
    [Fact]
    public void RaisingTheLimitClearsAPendingWarning()
    {
        var afk = new PracticeAfk();

        var warned = Run(afk, 0, 700, 15, _ => new[] { Still(Idle) });
        var after = Run(afk, 701, 2000, 30, _ => new[] { Still(Idle) });

        Assert.Equal(new[] { (PracticeAfk.Step.Warn, 600) }, Steps(warned));
        Assert.Equal(
            new[]
            {
                (PracticeAfk.Step.Warn, 1500),
                (PracticeAfk.Step.FinalWarn, 1740),
                (PracticeAfk.Step.Kick, 1800),
            },
            Steps(after)
        );
    }

    [Fact]
    public void TheKickSaysHowLongThePlayerWasActuallyIdle()
    {
        var afk = new PracticeAfk();
        var kicks = new List<PracticeAfk.Due>();

        for (int now = 0; now <= 700; now++)
        {
            afk.Sweep(new[] { Still(Idle) }, now, 15);
        }

        for (int now = 701; now <= 1000; now++)
        {
            kicks.AddRange(
                afk.Sweep(new[] { Still(Idle) }, now, 1).Where(due => due.Step == PracticeAfk.Step.Kick)
            );
        }

        Assert.Equal(900, Assert.Single(kicks).IdleSeconds);
        Assert.Contains("15 minutes", PracticeAfk.KickReason(kicks[0].IdleSeconds));
        Assert.Contains("90 seconds", PracticeAfk.KickReason(90));
    }

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(1441)]
    public void ALimitPastADayIsADay(int minutes)
    {
        var afk = new PracticeAfk();

        var due = Run(afk, 0, 1440 * 60, minutes, _ => new[] { Still(Idle) });

        Assert.Equal(
            new[]
            {
                (PracticeAfk.Step.Warn, 1435 * 60),
                (PracticeAfk.Step.FinalWarn, 1439 * 60),
                (PracticeAfk.Step.Kick, 1440 * 60),
            },
            Steps(due)
        );
    }

    [Fact]
    public void ANegativeLimitIsOff()
    {
        var afk = new PracticeAfk();

        Assert.Empty(Run(afk, 0, 7200, -5, _ => new[] { Still(Idle) }));
    }

    // A warning or kick that throws, for a player who has just left, must not
    // stop the next player's from going out.
    [Fact]
    public void OneFailedStepDoesNotStopTheRest()
    {
        var acted = new List<ulong>();
        var failed = new List<ulong>();

        PracticeAfk.Dispatch(
            new[]
            {
                new PracticeAfk.Due(Idle, PracticeAfk.Step.Kick, 0, 900),
                new PracticeAfk.Due(Busy, PracticeAfk.Step.Kick, 0, 900),
            },
            due =>
            {
                if (due.SteamId == Idle)
                {
                    throw new InvalidOperationException("the player is gone");
                }

                acted.Add(due.SteamId);
            },
            (due, _) => failed.Add(due.SteamId)
        );

        Assert.Equal(new[] { Busy }, acted);
        Assert.Equal(new[] { Idle }, failed);
    }

    [Theory]
    [InlineData("2147483647", 1440)]
    [InlineData("3000000000", 1440)]
    [InlineData("-5", 0)]
    [InlineData("7.9", 7)]
    public void TheSessionClampsTheIdleLimit(string sent, int read)
    {
        PracticeSessionData session = JsonSerializer
            .Deserialize<UtilitySessionRow>(
                $$"""
                {
                  "session_id": "11111111-1111-1111-1111-111111111111",
                  "match_id": "22222222-2222-2222-2222-222222222222",
                  "map_name": "de_nuke",
                  "password": "hunter2",
                  "steam_ids": ["76561198000000001"],
                  "afk_kick_minutes": {{sent}}
                }
                """,
                PracticeJson.Options
            )!
            .ToSession();

        Assert.Equal(read, session.afk_kick_minutes);
        Assert.Single(session.allowed_steam_ids);
    }

    [Fact]
    public void TheSessionCarriesTheIdleLimit()
    {
        PracticeSessionData session = JsonSerializer
            .Deserialize<UtilitySessionRow>(
                """
                {
                  "session_id": "11111111-1111-1111-1111-111111111111",
                  "match_id": "22222222-2222-2222-2222-222222222222",
                  "map_name": "de_nuke",
                  "password": "hunter2",
                  "steam_ids": [],
                  "afk_kick_minutes": 15
                }
                """,
                PracticeJson.Options
            )!
            .ToSession();

        Assert.Equal(15, session.afk_kick_minutes);
    }

    [Fact]
    public void AnApiThatSaysNothingLeavesTheKickOff()
    {
        PracticeSessionData session = JsonSerializer
            .Deserialize<UtilitySessionRow>(
                """
                {
                  "session_id": "11111111-1111-1111-1111-111111111111",
                  "match_id": "22222222-2222-2222-2222-222222222222",
                  "map_name": "de_nuke",
                  "password": "hunter2",
                  "steam_ids": []
                }
                """,
                PracticeJson.Options
            )!
            .ToSession();

        Assert.Equal(0, session.afk_kick_minutes);
    }
}
