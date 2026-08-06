/**
 * 5Stack CS:GO Plugin - Round Start Events
 * round_start -> "score" + round info
 */

#if defined _fivestack_event_round_start_included
    #endinput
#endif
#define _fivestack_event_round_start_included

public Action Event_RoundStart(Event event, const char[] name, bool dontBroadcast)
{
    if (!IsLive()) return Plugin_Continue;

    // Publish round information (score update)
    PublishRoundInformation();

    // Check player count — pause if players missing
    int expectedCount = GetExpectedPlayerCount();
    int actualCount = 0;
    for (int i = 1; i <= MaxClients; i++)
    {
        if (IsValidClient(i) && GetClientTeam(i) > CS_TEAM_SPECTATOR)
            actualCount++;
    }

    if (actualCount < expectedCount)
    {
        MatchManager_PauseMatch("Waiting for players to reconnect");
    }

    return Plugin_Continue;
}
