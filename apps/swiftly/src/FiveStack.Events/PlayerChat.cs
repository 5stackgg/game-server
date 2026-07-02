using FiveStack.Entities;
using FiveStack.Utilities;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace FiveStack;

public partial class FiveStackPlugin
{
    public HookResult OnPlayerChat(IPlayer? player, string message, bool teamOnly)
    {
        if (player == null || !player.IsValid)
        {
            return HookResult.Continue;
        }

        if (player.Controller.Team == Team.Spectator)
        {
            PublishChatEvent(player, message);

            _gameServer.Message(
                MessageType.Chat,
                $" [red]{player.Controller.Clan}[white] {player.Name}: {message}"
            );

            return HookResult.Stop;
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

        MatchMember? member = MatchUtility.GetMemberFromLineup(
            matchData,
            player.SteamID.ToString(),
            player.Name
        );

        if (member != null)
        {
            if (member.is_gagged)
            {
                return HookResult.Stop;
            }
        }

        PublishChatEvent(player, message);

        return HookResult.Continue;
    }

    private void PublishChatEvent(IPlayer player, string message)
    {
        _matchEvents.PublishGameEvent(
            "chat",
            new Dictionary<string, object>
            {
                { "player", player.SteamID.ToString() },
                { "message", message },
            }
        );
    }
}
