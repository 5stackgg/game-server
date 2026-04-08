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

        _surrenderSystem.surrenderingVote?.RemovePlayerVote(player.SteamID);
        _timeoutSystem.pauseVote?.RemovePlayerVote(player.SteamID);
        _timeoutSystem.resumeVote?.RemovePlayerVote(player.SteamID);
        _gameBackupRounds.restoreRoundVote?.RemovePlayerVote(player.SteamID);

        if (match.IsInProgress())
        {
            if (match.IsFreezePeriod())
            {
                match.PauseMatch("Player disconnected, pausing match");
            }

            _surrenderSystem.SetupDisconnectTimer(@event.Userid.Team, @event.Userid.SteamID);
        }

        return HookResult.Continue;
    }
}
