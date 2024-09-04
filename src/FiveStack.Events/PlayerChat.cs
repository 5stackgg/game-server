using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using FiveStack.Entities;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public partial class FiveStackPlugin
{
    public HookResult OnPlayerChat(CCSPlayerController? player, CommandInfo info)
    {
        this._logger.LogInformation("OK LETS GO");
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
