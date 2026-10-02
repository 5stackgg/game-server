using FiveStack.Entities;
using FiveStack.Utilities;
using Xunit;

public class CommunicationAbuseMuteTests
{
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
}
