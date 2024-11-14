using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using FiveStack.Entities;
using FiveStack.Utilities;

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

        if (match == null)
        {
            return HookResult.Continue;
        }

        MatchData? matchData = match.GetMatchData();

        if (matchData == null)
        {
            return HookResult.Continue;
        }

        MatchMember? member = MatchUtility.GetMemberFromLineup(matchData, player);

        if (member != null)
        {
            if (member.is_gagged)
            {
                return HookResult.Stop;
            }
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
