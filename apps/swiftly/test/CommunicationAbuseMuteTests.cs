using FiveStack.Entities;
using FiveStack.Utilities;
using Xunit;

public class CommunicationAbuseMuteTests
{
    private const ulong Penalised = 76561198000000001;
    private const ulong Other = 76561198000000002;
    private const string PenalisedId = "76561198000000001";
    private const string MutedId = "76561198000000003";
    private const string CoachId = "76561198000000004";

    private static MatchData BuildMatch()
    {
        return new MatchData
        {
            lineup_1 = new MatchLineUp
            {
                coach_steam_id = CoachId,
                lineup_players = new List<MatchMember>
                {
                    new MatchMember { steam_id = PenalisedId, name = "Penalised" },
                    new MatchMember
                    {
                        steam_id = MutedId,
                        name = "Muted",
                        is_muted = true,
                    },
                },
            },
            lineup_2 = new MatchLineUp
            {
                lineup_players = new List<MatchMember>
                {
                    new MatchMember
                    {
                        steam_id = null,
                        placeholder_name = "Placeholder",
                        is_muted = true,
                    },
                },
            },
        };
    }

    [Fact]
    public void ValveMuteIsClearedForALineupMemberWeLeftUnmuted()
    {
        Assert.True(
            CommunicationAbuseMute.ShouldClear(true, false, BuildMatch(), PenalisedId, "Penalised")
        );
    }

    [Fact]
    public void ValveMuteIsClearedForSomeoneOutsideTheLineups()
    {
        Assert.True(
            CommunicationAbuseMute.ShouldClear(true, false, BuildMatch(), CoachId, "Coach")
        );
    }

    [Fact]
    public void AMutedLineupMemberIsLeftAloneBeforeTheirVoiceFlagsAreApplied()
    {
        Assert.False(
            CommunicationAbuseMute.ShouldClear(true, false, BuildMatch(), MutedId, "Muted")
        );
    }

    [Fact]
    public void AMutedPlaceholderClaimedByNameIsLeftAlone()
    {
        Assert.False(
            CommunicationAbuseMute.ShouldClear(
                true,
                false,
                BuildMatch(),
                PenalisedId + "9",
                "Place"
            )
        );
    }

    [Fact]
    public void APlayerMutedThroughVoiceFlagsIsLeftAlone()
    {
        Assert.False(
            CommunicationAbuseMute.ShouldClear(true, true, BuildMatch(), PenalisedId, "Penalised")
        );
    }

    [Fact]
    public void NothingIsClearedWithoutAMatch()
    {
        Assert.False(
            CommunicationAbuseMute.ShouldClear(true, false, null, PenalisedId, "Penalised")
        );
    }

    [Fact]
    public void NothingToClearWithoutAValveMute()
    {
        Assert.False(
            CommunicationAbuseMute.ShouldClear(false, false, BuildMatch(), PenalisedId, "Penalised")
        );
    }

    [Fact]
    public void AWatchedPlayerIsRecheckedForTheWholeWindowThenDropped()
    {
        CommunicationAbuseMute recheck = new();
        recheck.Connected(Penalised);

        DrainWindow(recheck, Penalised);

        Assert.Empty(recheck.Due());
    }

    [Fact]
    public void ASpawnRestartsTheWindow()
    {
        CommunicationAbuseMute recheck = new();
        recheck.Connected(Penalised);

        for (int check = 0; check < CommunicationAbuseMute.RecheckCount - 1; check++)
        {
            recheck.Due();
        }

        recheck.Watch(Penalised);

        DrainWindow(recheck, Penalised);

        Assert.Empty(recheck.Due());
    }

    [Fact]
    public void AReassertRestartsTheWindowOnlyUpToTheCap()
    {
        CommunicationAbuseMute recheck = new();
        recheck.Connected(Penalised);

        for (int restart = 0; restart < CommunicationAbuseMute.MaxRestarts; restart++)
        {
            Assert.Contains(Penalised, recheck.Due());
            recheck.Reasserted(Penalised);
        }

        Assert.Contains(Penalised, recheck.Due());
        recheck.Reasserted(Penalised);

        for (int check = 1; check < CommunicationAbuseMute.RecheckCount; check++)
        {
            Assert.Contains(Penalised, recheck.Due());
        }

        Assert.Empty(recheck.Due());
    }

    [Fact]
    public void ReconnectingResetsTheCap()
    {
        CommunicationAbuseMute recheck = new();
        recheck.Connected(Penalised);

        for (int restart = 0; restart < CommunicationAbuseMute.MaxRestarts; restart++)
        {
            recheck.Due();
            recheck.Reasserted(Penalised);
        }

        recheck.Connected(Penalised);
        recheck.Due();
        recheck.Reasserted(Penalised);

        DrainWindow(recheck, Penalised);

        Assert.Empty(recheck.Due());
    }

    [Fact]
    public void OnlyTheFirstClearOfAConnectionIsAnnounced()
    {
        CommunicationAbuseMute recheck = new();
        recheck.Connected(Penalised);

        Assert.True(recheck.FirstClear(Penalised));
        Assert.False(recheck.FirstClear(Penalised));

        recheck.Connected(Penalised);

        Assert.True(recheck.FirstClear(Penalised));
    }

    [Fact]
    public void EachPlayerHasTheirOwnWindow()
    {
        CommunicationAbuseMute recheck = new();
        recheck.Connected(Penalised);
        recheck.Due();
        recheck.Connected(Other);

        for (int check = 1; check < CommunicationAbuseMute.RecheckCount; check++)
        {
            recheck.Due();
        }

        Assert.Equal([Other], recheck.Due());
        Assert.Empty(recheck.Due());
    }

    [Fact]
    public void NothingIsDueBeforeAnyoneIsWatched()
    {
        Assert.Empty(new CommunicationAbuseMute().Due());
    }

    private static void DrainWindow(CommunicationAbuseMute recheck, ulong steamId)
    {
        for (int check = 0; check < CommunicationAbuseMute.RecheckCount; check++)
        {
            Assert.Contains(steamId, recheck.Due());
        }
    }
}
