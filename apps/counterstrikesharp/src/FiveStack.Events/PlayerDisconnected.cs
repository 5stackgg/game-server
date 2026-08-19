using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using FiveStack.Entities;
using FiveStack.Utilities;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [GameEventHandler]
    public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        MatchMap? currentMap = match?.GetCurrentMap();
        MatchData? matchData = match?.GetMatchData();
        if (
            @event.Userid == null
            || !@event.Userid.IsValid
            || @event.Userid.IsBot
            || match == null
            || currentMap == null
            || matchData == null
        )
        {
            return HookResult.Continue;
        }

        CCSPlayerController player = @event.Userid;

        MatchMember? member = MatchUtility.GetMemberFromLineup(
            matchData,
            player.SteamID.ToString(),
            player.PlayerName
        );

        if (member == null)
        {
            return HookResult.Continue;
        }

        _matchEvents.PublishGameEvent(
            "player-disconnected",
            new Dictionary<string, object> { { "steam_id", player.SteamID.ToString() } }
        );

        if (match.IsWarmup() || match.IsKnife())
        {
            match.readySystem.UnreadyPlayer(player);
            match.captainSystem.RemoveCaptain(@event.Userid);
        }

        _surrenderSystem.RemovePlayerVoteOnDisconnect(player.SteamID);
        _timeoutSystem.RemovePlayerVoteOnDisconnect(player.SteamID);
        _gameBackupRounds.RemovePlayerVoteOnDisconnect(player.SteamID);

        if (match.IsInPlay())
        {
            if (match.IsFreezePeriod())
            {
                // Re-checked on each retry if a tactical timeout is holding the
                // pause off -- they may be back before it ends.
                match.PauseMatch(
                    "Player disconnected, pausing match",
                    stillRequired: () =>
                        ConnectedRosterCount(match.GetMatchData() ?? matchData)
                        < match.GetExpectedPlayerCount()
                );
            }

            _surrenderSystem.SetupDisconnectTimer(@event.Userid.Team, @event.Userid.SteamID);
        }

        return HookResult.Continue;
    }
}
