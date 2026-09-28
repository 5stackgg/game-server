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

        if (teamOnly)
        {
            RelayTeamChat(player, message);

            return HookResult.Continue;
        }

        if (player.Controller.Team == Team.Spectator)
        {
            PublishChatEvent(player, message);

            string clan = string.IsNullOrEmpty(player.Controller.Clan)
                ? ""
                : $"[{player.Controller.Clan}]";

            _gameServer.Message(
                MessageType.Chat,
                $" [red]{clan}[white] {player.Name}: {message}"
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

    private void RelayTeamChat(IPlayer player, string message)
    {
        MatchData? matchData = _matchService.GetCurrentMatch()?.GetMatchData();

        if (matchData == null)
        {
            return;
        }

        string? lineupId = MatchUtility.GetTeamChatRelayLineupId(
            matchData,
            player.SteamID.ToString(),
            player.Name
        );

        if (lineupId == null)
        {
            return;
        }

        PublishChatEvent(player, message, lineupId);
    }

    private void PublishChatEvent(IPlayer player, string message, string? teamLineupId = null)
    {
        _matchEvents.PublishGameEvent(
            "chat",
            MatchUtility.ChatEventData(player.SteamID.ToString(), message, teamLineupId)
        );
    }
}
