using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using FiveStack.Enums;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [ConsoleCommand("upload_demos", "upload demos")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public async void OnUploadDemo(CCSPlayerController? player, CommandInfo command)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        if (match == null)
        {
            return;
        }

        match.UpdateMapStatus(eMapStatus.UploadingDemo);

        await _gameDemos.UploadDemos();
    }

    [ConsoleCommand("test_start_demo", "start demo recording")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void StartDemo(CCSPlayerController? player, CommandInfo command)
    {
        _gameDemos.Start();
    }

    [ConsoleCommand("test_stop_demo", "stop demo recording")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void StopDemo(CCSPlayerController? player, CommandInfo command)
    {
        _gameDemos.Stop();
    }
}
