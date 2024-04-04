using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using FiveStack.Entities;
using FiveStack.Utilities;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [ConsoleCommand("get_match", "Gets match information from api")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void get_match(CCSPlayerController? player, CommandInfo command)
    {
        _matchService.GetMatch();
    }

    [ConsoleCommand("upload_demos", "upload demos")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public async void upload_demos(CCSPlayerController? player, CommandInfo command)
    {
        FiveStackMatch? match = _matchService.GetMatchData();
        if (match == null)
        {
            return;
        }
        await _matchDemos.UploadDemos(match);
    }

    [ConsoleCommand("upload_backup_round", "upload backup round")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public async void upload_backup_round(CCSPlayerController? player, CommandInfo command)
    {
        FiveStackMatch? match = _matchService.GetMatchData();
        if (match == null)
        {
            return;
        }

        string round = command.ArgByIndex(1);

        if (round == null)
        {
            return;
        }

        await _backUpManagement.UploadBackupRound(match, round);
    }

    [ConsoleCommand("match_state", "Forces a match to update its current state")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void SetMatchState(CCSPlayerController? player, CommandInfo command)
    {
        FiveStackMatch? match = _matchService.GetMatchData();
        if (match == null)
        {
            return;
        }
        _matchService.UpdateMapStatus(MatchUtility.MapStatusStringToEnum(command.ArgString));
    }
}
