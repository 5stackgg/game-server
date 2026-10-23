using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Utilities;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [GameEventHandler(HookMode.Post)]
    public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event)
    {
        if (@event.UserIdPlayer == null || !@event.UserIdPlayer.IsValid)
        {
            return HookResult.Continue;
        }

        ConnectedRoles.Remove(@event.UserIdPlayer.SteamID);

        MatchManager? match = _matchService.GetCurrentMatch();
        MatchMap? currentMap = match?.GetCurrentMap();
        MatchData? matchData = match?.GetMatchData();
        if (match == null || currentMap == null || matchData == null)
        {
            return HookResult.Continue;
        }

        IPlayer player = @event.UserIdPlayer;

        MatchMember? member = MatchUtility.GetMemberFromLineup(
            matchData,
            player.SteamID.ToString(),
            player.Name
        );

        eDisconnectHandling handling = PlayerDisconnectUtility.Handle(
            player.IsFakeClient,
            _overCapacityKicks.Remove(player.SteamID),
            member != null
        );

        if (handling == eDisconnectHandling.Ignore)
        {
            return HookResult.Continue;
        }

        _matchEvents.PublishGameEvent(
            "player-disconnected",
            new Dictionary<string, object> { { "steam_id", player.SteamID.ToString() } }
        );

        if (handling != eDisconnectHandling.LineupMemberLeft)
        {
            return HookResult.Continue;
        }

        if (match.IsWarmup() || match.IsKnife())
        {
            match.readySystem.UnreadyPlayer(player);
            match.captainSystem.PlayerLeft(player, match.IsKnife());
        }

        _surrenderSystem.RemovePlayerVoteOnDisconnect(player.SteamID);
        match.timeoutSystem.RemovePlayerVoteOnDisconnect(player.SteamID);
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
                        MatchUtility.ConnectedRosterCount(match.GetMatchData() ?? matchData)
                        < match.GetExpectedPlayerCount()
                );
            }

            _surrenderSystem.SetupDisconnectTimer(
                @event.UserIdPlayer.Controller.Team,
                @event.UserIdPlayer.SteamID
            );
        }

        return HookResult.Continue;
    }
}
