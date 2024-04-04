using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using FiveStack.Entities;
using FiveStack.Utilities;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [ConsoleCommand("restore_round", "Restores to a previous round")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void ResetRound(CCSPlayerController? player, CommandInfo command)
    {
        string round = command.ArgByIndex(1);
        FiveStackMatch? match = _matchService.GetMatchData();

        if (match == null || round == null)
        {
            return;
        }

        _backUpManagement.LoadRound(match, round);
    }

    [ConsoleCommand("css_reset", "Restores to a previous round")]
    public void RestoreRound(CCSPlayerController? player, CommandInfo command)
    {
        // TODO - round can be null, reset to -1 round
        string round = command.ArgByIndex(1);
        FiveStackMatch? match = _matchService.GetMatchData();

        if (match == null || round == null)
        {
            return;
        }

        bool isResttingRound = _backUpManagement.IsResttingRound();
        if (player != null && isResttingRound)
        {
            if (
                player.UserId != null
                && MatchUtility.GetMemberFromLineup(match, player)?.captain == true
            )
            {
                string vote = command.ArgByIndex(1);

                // TODO - different command to progress failure?
                // mabye just a .y / .n
                if (vote != null)
                {
                    _backUpManagement.VoteFailed();
                    return;
                }

                _backUpManagement.Vote(match, player);
            }

            return;
        }

        _backUpManagement.RestoreBackupRound(match, round, player);
    }
}
