using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using FiveStack.Entities;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [GameEventHandler]
    public HookResult BombPlaned(EventBombPlanted @event, GameEventInfo info)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();

        if (
            @event.Userid == null
            || !@event.Userid.IsValid
            || match == null
            || matchData?.current_match_map_id == null
            || !match.IsLive()
        )
        {
            return HookResult.Continue;
        }

        CCSPlayerController planter = @event.Userid;

        _matchEvents.PublishGameEvent(
            "objective",
            new Dictionary<string, object>
            {
                { "time", DateTime.Now },
                { "match_map_id", matchData.current_match_map_id },
                { "round", _gameServer.GetCurrentRound() },
                { "type", "Planted" },
                { "player_steam_id", planter.SteamID.ToString() },
            }
        );

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult BombPlaned(EventBombDefused @event, GameEventInfo info)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();

        if (
            @event.Userid == null
            || !@event.Userid.IsValid
            || match == null
            || matchData?.current_match_map_id == null
            || !match.IsLive()
        )
        {
            return HookResult.Continue;
        }

        CCSPlayerController defuser = @event.Userid;

        _matchEvents.PublishGameEvent(
            "objective",
            new Dictionary<string, object>
            {
                { "time", DateTime.Now },
                { "match_map_id", matchData.current_match_map_id },
                { "round", _gameServer.GetCurrentRound() },
                { "type", "Defused" },
                { "player_steam_id", defuser.SteamID.ToString() },
            }
        );

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult BombExploded(EventBombExploded @event, GameEventInfo info)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();

        if (
            @event.Userid == null
            || !@event.Userid.IsValid
            || match == null
            || matchData?.current_match_map_id == null
            || !match.IsLive()
        )
        {
            return HookResult.Continue;
        }

        CCSPlayerController bomber = @event.Userid;

        _matchEvents.PublishGameEvent(
            "objective",
            new Dictionary<string, object>
            {
                { "time", DateTime.Now },
                { "match_map_id", matchData.current_match_map_id },
                { "round", _gameServer.GetCurrentRound() },
                { "type", "Exploded" },
                { "site", @event.Site },
                { "player_steam_id", bomber.SteamID.ToString() },
            }
        );

        return HookResult.Continue;
    }
}
