using System.Text.Json;
using FiveStack.Enums;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using Xunit;

// Read off the game thread, before the answer is handed to it: a body that is
// not a match must be logged here, not thrown inside a frame callback.
public class CurrentMatchUtilityTests
{
    private const string ServerId = "11111111-1111-1111-1111-111111111111";

    private static readonly Guid MatchId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private sealed class Logged : ILogger
    {
        public readonly List<(LogLevel Level, Exception? Exception, string Message)> Entries =
            new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            Entries.Add((logLevel, exception, formatter(state, exception)));
        }
    }

    [Fact]
    public void AnEmptyBodyClearsTheMatch()
    {
        CurrentMatchDecision decision = CurrentMatchUtility.Read("", ServerId, new Logged());

        Assert.Equal(eCurrentMatchAction.Clear, decision.Action);
        Assert.Null(decision.Match);
    }

    [Fact]
    public void ANullMatchIsLoggedAndLeavesTheMatchAlone()
    {
        Logged logged = new();

        CurrentMatchDecision decision = CurrentMatchUtility.Read("null", ServerId, logged);

        Assert.Equal(eCurrentMatchAction.Ignore, decision.Action);
        Assert.Contains(
            logged.Entries,
            entry => entry.Level == LogLevel.Error && entry.Message.Contains(ServerId)
        );
    }

    [Fact]
    public void ABodyThatIsNotJsonIsLoggedAndLeavesTheMatchAlone()
    {
        Logged logged = new();

        CurrentMatchDecision decision = CurrentMatchUtility.Read("{", ServerId, logged);

        Assert.Equal(eCurrentMatchAction.Ignore, decision.Action);
        Assert.Contains(logged.Entries, entry => entry.Exception is JsonException);
    }

    [Fact]
    public void AMatchIsSetUp()
    {
        CurrentMatchDecision decision = CurrentMatchUtility.Read(
            $$"""{ "id": "{{MatchId}}", "is_lan": true }""",
            ServerId,
            new Logged()
        );

        Assert.Equal(eCurrentMatchAction.Setup, decision.Action);
        Assert.Equal(MatchId, decision.Match?.id);
        Assert.True(decision.Match?.is_lan);
    }
}
