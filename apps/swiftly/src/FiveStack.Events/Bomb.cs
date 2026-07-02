using FiveStack.Entities;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [GameEventHandler(HookMode.Post)]
    public HookResult BombPlaned(EventBombPlanted @event)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();

        if (
            @event.UserIdPlayer == null
            || !@event.UserIdPlayer.IsValid
            || match == null
            || matchData?.current_match_map_id == null
            || !match.IsInPlay()
        )
        {
            return HookResult.Continue;
        }

        IPlayer planter = @event.UserIdPlayer;

        _matchEvents.PublishGameEvent(
            "objective",
            new Dictionary<string, object>
            {
                { "time", DateTime.Now },
                { "match_map_id", match.GetActiveMapId() ?? matchData.current_match_map_id },
                { "round", _gameServer.GetCurrentRound() },
                { "type", "Planted" },
                { "player_steam_id", planter.SteamID.ToString() },
            }
        );

        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Post)]
    public HookResult BombDefused(EventBombDefused @event)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();

        if (
            @event.UserIdPlayer == null
            || !@event.UserIdPlayer.IsValid
            || match == null
            || matchData?.current_match_map_id == null
            || !match.IsInPlay()
        )
        {
            return HookResult.Continue;
        }

        IPlayer defuser = @event.UserIdPlayer;

        _matchEvents.PublishGameEvent(
            "objective",
            new Dictionary<string, object>
            {
                { "time", DateTime.Now },
                { "match_map_id", match.GetActiveMapId() ?? matchData.current_match_map_id },
                { "round", _gameServer.GetCurrentRound() },
                { "type", "Defused" },
                { "player_steam_id", defuser.SteamID.ToString() },
            }
        );

        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Post)]
    public HookResult BombExploded(EventBombExploded @event)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();

        if (
            @event.UserIdPlayer == null
            || !@event.UserIdPlayer.IsValid
            || match == null
            || matchData?.current_match_map_id == null
            || !match.IsInPlay()
        )
        {
            return HookResult.Continue;
        }

        IPlayer bomber = @event.UserIdPlayer;

        _matchEvents.PublishGameEvent(
            "objective",
            new Dictionary<string, object>
            {
                { "time", DateTime.Now },
                { "match_map_id", match.GetActiveMapId() ?? matchData.current_match_map_id },
                { "round", _gameServer.GetCurrentRound() },
                { "type", "Exploded" },
                { "site", @event.Site },
                { "player_steam_id", bomber.SteamID.ToString() },
            }
        );

        return HookResult.Continue;
    }
}
