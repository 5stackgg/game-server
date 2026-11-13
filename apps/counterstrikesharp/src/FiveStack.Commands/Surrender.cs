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
        Guid? callerLineup =
            matchData == null
                ? null
                : LineupMembership.LineupOf(matchData, player.SteamID.ToString(), player.PlayerName);

        if (!SurrenderSystem.CanCallSurrender(player.Team, callerLineup != null))
        {
            player.PrintToConsole(_gameServer.Localize(player, "surrender.not_in_match"));
            return;
        }

        _surrenderSystem.SetupSurrender(player.Team, player, callerLineup);
    }
}
