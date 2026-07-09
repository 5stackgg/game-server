using FiveStack.Entities;
using FiveStack.Utilities;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;

namespace FiveStack;

public partial class FiveStackPlugin
{
    public HookResult GagPlayer(IPlayer? player, string message, bool teamOnly)
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

        MatchMember? member = MatchUtility.GetMemberFromLineup(
            matchData,
            player.SteamID.ToString(),
            player.Name
        );

        if (member != null)
        {
            if (member.is_gagged)
            {
                _gameServer.Message(MessageType.Chat, _localizer["gag.you_are_gagged"], player);
                return HookResult.Stop;
            }
        }

        return HookResult.Continue;
    }
}
