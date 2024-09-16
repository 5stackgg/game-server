using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using FiveStack.Entities;

namespace FiveStack;

public partial class FiveStackPlugin
{
    public HookResult OnPlayerChat(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !player.IsValid)
        {
            return HookResult.Continue;
        }

        MatchManager? match = _matchService.GetCurrentMatch();
        MatchMap? currentMap = match?.GetCurrentMap();

        if (match == null)
        {
            return HookResult.Continue;
        }

        _matchEvents.PublishGameEvent(
            "chat",
            new Dictionary<string, object>
            {
                { "player", player.SteamID.ToString() },
                { "message", info.ArgString.TrimStart('"') },
            }
        );

        return HookResult.Continue;
    }
}
