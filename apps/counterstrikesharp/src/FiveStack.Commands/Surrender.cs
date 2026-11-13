using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using FiveStack.Entities;
using FiveStack.Utilities;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [ConsoleCommand("css_surrender", "Initiates a surrender vote for the player's team")]
    public void Surrender(CCSPlayerController? player, CommandInfo? command)
    {
        if (player == null)
        {
            return;
        }

        MatchData? matchData = _matchService.GetCurrentMatch()?.GetMatchData();
        bool isLineupMember =
            matchData != null
            && MatchUtility.GetMemberFromLineup(
                matchData,
                player.SteamID.ToString(),
                player.PlayerName
            ) != null;

        if (!SurrenderSystem.CanCallSurrender(player.Team, isLineupMember))
        {
            player.PrintToConsole(" Only a player in this match can call a surrender");
            return;
        }

        _surrenderSystem.SetupSurrender(player.Team, player);
    }
}
