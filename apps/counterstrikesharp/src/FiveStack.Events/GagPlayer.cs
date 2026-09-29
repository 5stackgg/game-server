using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using FiveStack.Entities;
using FiveStack.Utilities;

namespace FiveStack;

public partial class FiveStackPlugin
{
    public HookResult GagPlayer(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !player.IsValid)
        {
            return HookResult.Continue;
        }

        MatchManager? match = _matchService.GetCurrentMatch();

        if (match == null)
        {
            return HookResult.Continue;
        }

        MatchData? matchData = match?.GetMatchData();

        if (matchData == null)
        {
            return HookResult.Continue;
        }

        if (MatchUtility.IsGagged(matchData, player.SteamID.ToString(), player.PlayerName))
        {
            player.PrintToChat(Localizer["gag.you_are_gagged"]);
            return HookResult.Stop;
        }

        return HookResult.Continue;
    }
}
