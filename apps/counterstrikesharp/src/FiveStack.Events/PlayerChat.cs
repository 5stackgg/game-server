using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using FiveStack.Utilities;

namespace FiveStack;

public partial class FiveStackPlugin
{
    public HookResult OnPlayerChat(CCSPlayerController? player, CommandInfo info)
    {
        return HandlePlayerChat(player, info, false);
    }

    public HookResult OnPlayerTeamChat(CCSPlayerController? player, CommandInfo info)
    {
        return HandlePlayerChat(player, info, true);
    }

    private HookResult HandlePlayerChat(
        CCSPlayerController? player,
        CommandInfo info,
        bool teamOnly
    )
    {
        if (player == null || !player.IsValid)
        {
            return HookResult.Continue;
        }

        string message = info.ArgString.Trim('"');

        if (teamOnly)
        {
            RelayTeamChat(player, message);

            // CSS skips the remaining say_team listeners once one returns
            // Handled or Stop, and GagPlayer after this one is what blocks a
            // gagged speaker in game and tells them why.
            return HookResult.Continue;
        }

        if (player.Team == CsTeam.Spectator)
        {
            PublishChatEvent(player, message);

            string clan = string.IsNullOrEmpty(player.Clan) ? "" : $"[{player.Clan}]";

            _gameServer.Message(
                HudDestination.Chat,
                $" {ChatColors.Red}{clan}{ChatColors.White} {player.PlayerName}: {message}"
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
            player.PlayerName
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

    private void RelayTeamChat(CCSPlayerController player, string message)
    {
        MatchData? matchData = _matchService.GetCurrentMatch()?.GetMatchData();

        if (matchData == null)
        {
            return;
        }

        (string Event, Dictionary<string, object> Data)? teamChat = MatchUtility.TeamChatEvent(
            matchData,
            player.SteamID.ToString(),
            player.PlayerName,
            message
        );

        if (teamChat == null)
        {
            return;
        }

        _matchEvents.PublishGameEvent(teamChat.Value.Event, teamChat.Value.Data);
    }

    private void PublishChatEvent(CCSPlayerController player, string message)
    {
        (string eventName, Dictionary<string, object> data) = MatchUtility.ChatEvent(
            player.SteamID.ToString(),
            message
        );

        _matchEvents.PublishGameEvent(eventName, data);
    }
}
