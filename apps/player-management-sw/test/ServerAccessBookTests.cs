using System.Runtime.InteropServices;
using FiveStack.Enums;
using FiveStack.Utilities;
using Xunit;

public class ServerAccessBookTests
{
    // Nothing loaded must not read as "everyone is welcome" nor as "nobody
    // is": the engine's own checks stay the gate until the panel has answered.
    [Fact]
    public void BeforeAnyListLoadsTheEngineDecides()
    {
        ServerAccessBook book = new();

        Assert.Equal(eServerAccess.Unknown, book.Decide("1"));
        Assert.False(book.Snapshot().Loaded);
    }

    [Fact]
    public void AnOpenServerLetsEveryoneIn()
    {
        ServerAccessBook book = new();
        book.Load(false, "open", ["1"]);

        Assert.Equal(eServerAccess.Open, book.Decide("1"));
        Assert.Equal(eServerAccess.Open, book.Decide("2"));
        Assert.Equal(0, book.Snapshot().Allowed);
    }

    [Fact]
    public void ARestrictedServerLetsInOnlyItsList()
    {
        ServerAccessBook book = new();
        book.Load(true, "v1", ["1", " 2 ", ""]);

        Assert.Equal(eServerAccess.Allowed, book.Decide("1"));
        Assert.Equal(eServerAccess.Allowed, book.Decide("2"));
        Assert.Equal(eServerAccess.Denied, book.Decide("3"));
        Assert.Equal(eServerAccess.Denied, book.Decide("0"));
        Assert.Equal(new ServerAccessSnapshot(true, true, "v1", 2, null), book.Snapshot());
    }

    [Fact]
    public void AFailedRefreshKeepsTheLastListAndIsReportedOnce()
    {
        ServerAccessBook book = new();
        book.Load(true, "v1", ["1"]);

        Assert.True(book.FetchFailed("503 down"));
        Assert.False(book.FetchFailed("503 down"));

        Assert.Equal(eServerAccess.Allowed, book.Decide("1"));
        Assert.Equal(eServerAccess.Denied, book.Decide("2"));
        Assert.True(book.IsCurrent("v1"));
        Assert.Equal(new ServerAccessSnapshot(true, true, "v1", 1, "503 down"), book.Snapshot());
    }

    [Fact]
    public void AFailedFetchBeforeAnyListStillLeavesItToTheEngine()
    {
        ServerAccessBook book = new();

        book.FetchFailed("connection refused");

        Assert.Equal(eServerAccess.Unknown, book.Decide("1"));
    }

    [Fact]
    public void ANewVersionReplacesTheListAndClearsTheError()
    {
        ServerAccessBook book = new();
        Assert.True(book.Load(true, "v1", ["1"]));
        book.FetchFailed("503 down");

        Assert.True(book.Load(true, "v2", ["2"]));

        Assert.Equal(eServerAccess.Denied, book.Decide("1"));
        Assert.Equal(eServerAccess.Allowed, book.Decide("2"));
        Assert.True(book.IsCurrent("v2"));
        Assert.False(book.IsCurrent("v1"));
        Assert.Null(book.Snapshot().Error);
    }

    [Fact]
    public void ReloadingTheSameVersionIsNotAChange()
    {
        ServerAccessBook book = new();
        book.Load(true, "v1", ["1"]);

        Assert.False(book.Load(true, "v1", ["1"]));
        Assert.True(book.Load(false, "v1", []));
    }

    [Fact]
    public void ADenialCoversOnlyThePlayersThePanelWasAskedAbout()
    {
        ServerAccessBook book = new();
        book.Answered(["1", "2"], ["2"], null);

        Assert.True(book.IsDenied("2"));
        Assert.False(book.IsDenied("1"));

        book.Answered(["1"], [], null);
        Assert.True(book.IsDenied("2"));

        book.Answered(["2"], [], null);
        Assert.False(book.IsDenied("2"));
    }

    [Fact]
    public void LeavingForgetsADenial()
    {
        ServerAccessBook book = new();
        book.Answered(["1"], ["1"], null);

        book.Left("1");

        Assert.False(book.IsDenied("1"));
    }

    [Fact]
    public void AKickReasonCarriesThePanelsMessageWithoutChatFormatting()
    {
        ServerAccessBook book = new();
        Assert.Equal("private", book.KickReason("private"));

        book.Answered([], [], "[red]Members only");

        Assert.Equal("private - Members only", book.KickReason("private"));
    }

    [Fact]
    public void TheSteamIdLeadsTheAuthTicket()
    {
        byte[] ticket = new byte[24];
        BitConverter.GetBytes(76561198000000001UL).CopyTo(ticket, 0);
        GCHandle pinned = GCHandle.Alloc(ticket, GCHandleType.Pinned);

        try
        {
            nint address = pinned.AddrOfPinnedObject();

            Assert.Equal(76561198000000001UL, ServerAccessBook.TicketSteamId(address, 24));
            Assert.Equal(0UL, ServerAccessBook.TicketSteamId(address, 7));
            Assert.Equal(0UL, ServerAccessBook.TicketSteamId(nint.Zero, 24));
        }
        finally
        {
            pinned.Free();
        }
    }
}
