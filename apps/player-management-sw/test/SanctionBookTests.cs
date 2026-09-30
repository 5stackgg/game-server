using FiveStack.Entities.PlayerManagement;
using FiveStack.Enums;
using FiveStack.Utilities;
using Xunit;

public class SanctionBookTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    private static PlayerSanction Sanction(
        string steamId,
        string type,
        DateTimeOffset? expiresAt = null,
        string? reason = null
    )
    {
        return new PlayerSanction
        {
            steam_id = steamId,
            type = type,
            expires_at = expiresAt,
            reason = reason,
        };
    }

    [Fact]
    public void APlayerThePanelWasAskedAboutAndSaysNothingOfIsCleared()
    {
        SanctionBook book = new();
        book.Record(["1"], [Sanction("1", "mute")]);

        book.Record(["1"], []);

        Assert.Equal(SanctionState.None, book.StateFor("1", Now));
    }

    // A banned player who left is not in the next sync, and their ban must
    // still be known if they rejoin while the panel is unreachable.
    [Fact]
    public void APlayerTheSyncDidNotAskAboutKeepsWhatWasKnown()
    {
        SanctionBook book = new();
        book.Record(["1", "2"], [Sanction("1", "ban"), Sanction("2", "gag")]);

        book.Record(["2"], []);

        Assert.True(book.StateFor("1", Now).IsBanned);
        Assert.False(book.StateFor("2", Now).IsGagged);
    }

    [Fact]
    public void SilenceIsBothAMuteAndAGag()
    {
        SanctionBook book = new();
        book.Record(["1"], [Sanction("1", "silence")]);

        SanctionState state = book.StateFor("1", Now);

        Assert.True(state.IsMuted);
        Assert.True(state.IsGagged);
        Assert.False(state.IsBanned);
    }

    [Fact]
    public void AnExpiredSanctionNoLongerApplies()
    {
        SanctionBook book = new();
        book.Record(["1"], [Sanction("1", "mute", Now.AddSeconds(30))]);

        Assert.True(book.StateFor("1", Now).IsMuted);
        Assert.False(book.StateFor("1", Now.AddSeconds(30)).IsMuted);
    }

    [Fact]
    public void APermanentSanctionOutranksATimedOne()
    {
        SanctionBook book = new();
        book.Record(
            ["1"],
            [Sanction("1", "gag", Now.AddDays(3), "timed"), Sanction("1", "gag", null, "permanent")]
        );

        Assert.Equal("permanent", book.StateFor("1", Now).Gag!.reason);
    }

    [Fact]
    public void OfTwoTimedSanctionsTheLaterExpiryIsTheOneBeingServed()
    {
        SanctionBook book = new();
        book.Record(
            ["1"],
            [
                Sanction("1", "mute", Now.AddHours(1), "short"),
                Sanction("1", "silence", Now.AddDays(1), "long"),
            ]
        );

        Assert.Equal("long", book.StateFor("1", Now).Mute!.reason);
    }

    [Fact]
    public void AnUnknownTypeIsIgnored()
    {
        SanctionBook book = new();
        book.Record(["1"], [Sanction("1", "warning")]);

        Assert.Equal(SanctionState.None, book.StateFor("1", Now));
    }

    [Fact]
    public void AJoiningPlayerAwaitsUntilThePanelAnswersForThem()
    {
        SanctionBook book = new();
        book.Joined("1");

        Assert.True(book.IsAwaiting("1"));
        Assert.Equal(["1"], book.Awaiting());

        book.Record(["1"], []);

        Assert.False(book.IsAwaiting("1"));
    }

    [Fact]
    public void AnAnswerAboutOtherPlayersLeavesAJoiningPlayerAwaiting()
    {
        SanctionBook book = new();
        book.Joined("1");

        book.Record(["2"], []);

        Assert.True(book.IsAwaiting("1"));
    }

    [Fact]
    public void AnUnansweredSyncFallsBackToWhatWasKnown()
    {
        SanctionBook book = new();
        book.Record(["1"], [Sanction("1", "ban")]);
        book.Joined("1");

        book.Unanswered(["1"]);

        Assert.False(book.IsAwaiting("1"));
        Assert.True(book.StateFor("1", Now).IsBanned);
    }

    [Fact]
    public void LeavingStopsAwaiting()
    {
        SanctionBook book = new();
        book.Joined("1");

        book.Left("1");

        Assert.Empty(book.Awaiting());
    }

    [Fact]
    public void ChangesReportOnlyTransitionsForMuteAndGag()
    {
        SanctionState muted = new(null, Sanction("1", "mute"), null);
        SanctionState gagged = new(null, null, Sanction("1", "gag"));

        Assert.Equal(eSanctionChange.Muted, SanctionState.Changes(SanctionState.None, muted));
        Assert.Equal(eSanctionChange.None, SanctionState.Changes(muted, muted));
        Assert.Equal(
            eSanctionChange.Unmuted | eSanctionChange.Gagged,
            SanctionState.Changes(muted, gagged)
        );
        Assert.Equal(eSanctionChange.Ungagged, SanctionState.Changes(gagged, SanctionState.None));
    }

    // A banned player still on the server has to be kicked, whether or not the
    // last pass already saw the ban.
    [Fact]
    public void ABanIsReportedEveryTimeItIsInForce()
    {
        SanctionState banned = new(Sanction("1", "ban"), null, null);

        Assert.True(SanctionState.Changes(banned, banned).HasFlag(eSanctionChange.Banned));
    }

    [Fact]
    public void AKickReasonCarriesTheReasonWithoutChatFormatting()
    {
        Assert.Equal("Banned", SanctionBook.KickReason(Sanction("1", "ban")));
        Assert.Equal(
            "Banned: cheating",
            SanctionBook.KickReason(Sanction("1", "ban", reason: "[red]cheating"))
        );
    }

    [Fact]
    public void UntilIsUtcAndEmptyForAPermanentSanction()
    {
        Assert.Equal("", SanctionBook.Until(Sanction("1", "mute")));
        Assert.Equal(
            "2026-09-29 14:30 UTC",
            SanctionBook.Until(
                Sanction(
                    "1",
                    "mute",
                    new DateTimeOffset(2026, 9, 29, 16, 30, 0, TimeSpan.FromHours(2))
                )
            )
        );
    }
}
