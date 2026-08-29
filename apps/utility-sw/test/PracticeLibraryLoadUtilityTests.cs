using FiveStack.Utilities;
using Xunit;

// The drain runs once a second for every player on the server, against a panel
// that can be slow, down or answering about a map the server has already left.
// Every one of those turns into a request nobody asked for if the rules here
// are wrong, so they are pinned rather than left in the plugin.
public class PracticeLibraryLoadUtilityTests
{
    private static readonly DateTime Now = new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void FetchesForAPlayerWhoHasNothing()
    {
        Assert.True(PracticeLibraryLoadUtility.ShouldFetch("de_mirage", null, null, null, Now));
    }

    [Fact]
    public void LeavesAPlayerWhoAlreadyHasThisMapAlone()
    {
        Assert.False(
            PracticeLibraryLoadUtility.ShouldFetch("de_mirage", "de_mirage", null, null, Now)
        );
    }

    // The whole reason the load is keyed by map: an answer that arrived for the
    // map the server was leaving is not an answer for the one it is on.
    [Fact]
    public void FetchesAgainAfterAMapChange()
    {
        Assert.True(
            PracticeLibraryLoadUtility.ShouldFetch("de_nuke", "de_mirage", null, null, Now)
        );
    }

    // A cold boot has no map until OnMapLoad. Asking for the library on "" is a
    // wasted round trip that then gets recorded as this player's answer.
    [Fact]
    public void WaitsForTheMapToBeKnown()
    {
        Assert.False(PracticeLibraryLoadUtility.ShouldFetch("", null, null, null, Now));
    }

    [Fact]
    public void DoesNotStackASecondFetchOnAnOutstandingOne()
    {
        Assert.False(
            PracticeLibraryLoadUtility.ShouldFetch(
                "de_mirage",
                null,
                Now.AddSeconds(-5),
                null,
                Now
            )
        );
    }

    // The failure this exists for: an answer that never lands leaves the
    // in-flight marker set, and a marker only ever cleared by an answer stops
    // that player's library loading for the rest of the process.
    [Fact]
    public void GivesUpOnAFetchThatNeverAnswered()
    {
        DateTime issued = Now.AddSeconds(-PracticeLibraryLoadUtility.FetchTimeoutSeconds - 1);

        Assert.True(PracticeLibraryLoadUtility.IsFetchExpired(issued, Now));
        Assert.True(
            PracticeLibraryLoadUtility.ShouldFetch("de_mirage", null, issued, null, Now)
        );
    }

    [Fact]
    public void ARequestInsideTheTimeoutIsStillAlive()
    {
        DateTime issued = Now.AddSeconds(-PracticeLibraryLoadUtility.FetchTimeoutSeconds + 1);

        Assert.False(PracticeLibraryLoadUtility.IsFetchExpired(issued, Now));
    }

    [Fact]
    public void HoldsOffUntilTheBackoffHasRunOut()
    {
        Assert.False(
            PracticeLibraryLoadUtility.ShouldFetch(
                "de_mirage",
                null,
                null,
                Now.AddSeconds(3),
                Now
            )
        );

        Assert.True(
            PracticeLibraryLoadUtility.ShouldFetch(
                "de_mirage",
                null,
                null,
                Now.AddSeconds(-1),
                Now
            )
        );
    }

    // An unreachable panel used to cost one request per player per second for
    // the life of the server. Ten people on a practice box whose panel is down
    // is a request storm, and it never stopped on its own.
    [Fact]
    public void BacksOffFurtherOnEveryFailure()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), PracticeLibraryLoadUtility.RetryDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(4), PracticeLibraryLoadUtility.RetryDelay(2));
        Assert.Equal(TimeSpan.FromSeconds(8), PracticeLibraryLoadUtility.RetryDelay(3));
        Assert.Equal(TimeSpan.FromSeconds(16), PracticeLibraryLoadUtility.RetryDelay(4));
    }

    // Capped rather than unbounded: a panel that comes back after an hour is
    // picked up within the minute, not the next day.
    [Fact]
    public void NeverWaitsLongerThanAMinute()
    {
        Assert.Equal(
            TimeSpan.FromSeconds(PracticeLibraryLoadUtility.MaxRetrySeconds),
            PracticeLibraryLoadUtility.RetryDelay(10)
        );

        Assert.Equal(
            TimeSpan.FromSeconds(PracticeLibraryLoadUtility.MaxRetrySeconds),
            PracticeLibraryLoadUtility.RetryDelay(int.MaxValue)
        );
    }

    // A server left running against a dead panel reaches a failure count that
    // overflows a naive shift, and a negative delay retries instantly forever.
    [Fact]
    public void NeverReturnsANegativeDelay()
    {
        foreach (int failures in new[] { 0, 1, 31, 32, 33, 64, int.MaxValue })
        {
            Assert.True(PracticeLibraryLoadUtility.RetryDelay(failures) > TimeSpan.Zero);
        }
    }
}
