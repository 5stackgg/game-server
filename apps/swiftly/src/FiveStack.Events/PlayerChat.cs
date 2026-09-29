using FiveStack.Entities;
using FiveStack.Enums;
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

        eAllChatRoute route = MatchUtility.AllChatRoute(
            _matchService.GetCurrentMatch()?.GetMatchData(),
            player.SteamID.ToString(),
            player.Name,
            player.Controller.Team == Team.Spectator
        );

        if (route == eAllChatRoute.Block)
        {
            return HookResult.Stop;
        }

        if (route == eAllChatRoute.Spectator)
        {
            PublishChatEvent(player, message);

            string clan = string.IsNullOrEmpty(player.Controller.Clan)
                ? ""
                : ChatUtility.StripFormatting($"[{player.Controller.Clan}]");

            string name = ChatUtility.StripFormatting(player.Name);
            string text = ChatUtility.StripFormatting(message);

            _gameServer.Message(MessageType.Chat, $" [red]{clan}[white] {name}: {text}");

            return HookResult.Stop;
        }

        if (route == eAllChatRoute.Publish)
        {
            PublishChatEvent(player, message);
        }

        return HookResult.Continue;
    }

    private void RelayTeamChat(IPlayer player, string message)
    {
        MatchData? matchData = _matchService.GetCurrentMatch()?.GetMatchData();

        if (matchData == null)
        {
            return;
        }

        (string Event, Dictionary<string, object> Data)? teamChat = MatchUtility.TeamChatEvent(
            matchData,
            player.SteamID.ToString(),
            player.Name,
            message
        );

        if (teamChat == null)
        {
            return;
        }

        _matchEvents.PublishGameEvent(teamChat.Value.Event, teamChat.Value.Data);
    }

    private void PublishChatEvent(IPlayer player, string message)
    {
        (string eventName, Dictionary<string, object> data) = MatchUtility.ChatEvent(
            player.SteamID.ToString(),
            message
        );

        _matchEvents.PublishGameEvent(eventName, data);
    }
}
