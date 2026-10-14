using System.Text.Json;
using FiveStack.Entities;
using FiveStack.Enums;
using Microsoft.Extensions.Logging;

namespace FiveStack.Utilities;

public sealed record CurrentMatchDecision(eCurrentMatchAction Action, MatchData? Match = null);

// Both match plugins read the api's answer here, off the game thread and before
// it is handed over, so a body that is not a match is logged rather than thrown
// inside a frame callback.
public static class CurrentMatchUtility
{
    public static CurrentMatchDecision Read(string body, string serverId, ILogger logger)
    {
        if (body.Length == 0)
        {
            return new CurrentMatchDecision(eCurrentMatchAction.Clear);
        }

        try
        {
            MatchData? matchData = JsonSerializer.Deserialize<MatchData>(body);

            if (matchData == null)
            {
                logger.LogError($"Match info for server {serverId} was null");
                return new CurrentMatchDecision(eCurrentMatchAction.Ignore);
            }

            return new CurrentMatchDecision(eCurrentMatchAction.Setup, matchData);
        }
        catch (JsonException ex)
        {
            logger.LogCritical(ex, $"Match info for server {serverId} is not valid JSON");
            return new CurrentMatchDecision(eCurrentMatchAction.Ignore);
        }
    }
}
