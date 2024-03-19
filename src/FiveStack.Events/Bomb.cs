using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [GameEventHandler]
    public HookResult BombPlaned(EventBombPlanted @event, GameEventInfo info)
    {
        if (@event.Userid == null || !@event.Userid.IsValid || _matchData == null || !IsLive())
        {
            return HookResult.Continue;
        }

        CCSPlayerController planter = @event.Userid;

        _redis.PublishMatchEvent(
            _matchData.id,
            new Redis.EventData<Dictionary<string, object>>
            {
                @event = "objective",
                data = new Dictionary<string, object>
                {
                    { "time", DateTime.Now },
                    { "match_map_id", _matchData.current_match_map_id },
                    { "round", _currentRound + 1 },
                    { "type", "Planted" },
                    { "player_steam_id", planter.SteamID.ToString() },
                }
            }
        );

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult BombPlaned(EventBombDefused @event, GameEventInfo info)
    {
        if (@event.Userid == null || !@event.Userid.IsValid || _matchData == null || !IsLive())
        {
            return HookResult.Continue;
        }

        CCSPlayerController defuser = @event.Userid;

        _redis.PublishMatchEvent(
            _matchData.id,
            new Redis.EventData<Dictionary<string, object>>
            {
                @event = "objective",
                data = new Dictionary<string, object>
                {
                    { "time", DateTime.Now },
                    { "match_map_id", _matchData.current_match_map_id },
                    { "round", _currentRound + 1 },
                    { "type", "Defused" },
                    { "player_steam_id", defuser.SteamID.ToString() },
                }
            }
        );

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult BombExploded(EventBombExploded @event, GameEventInfo info)
    {
        if (@event.Userid == null || !@event.Userid.IsValid || _matchData == null || !IsLive())
        {
            return HookResult.Continue;
        }

        CCSPlayerController bomber = @event.Userid;

        _redis.PublishMatchEvent(
            _matchData.id,
            new Redis.EventData<Dictionary<string, object>>
            {
                @event = "objective",
                data = new Dictionary<string, object>
                {
                    { "time", DateTime.Now },
                    { "match_map_id", _matchData.current_match_map_id },
                    { "round", _currentRound + 1 },
                    { "type", "Exploded" },
                    { "player_steam_id", bomber.SteamID.ToString() },
                }
            }
        );

        return HookResult.Continue;
    }
}
