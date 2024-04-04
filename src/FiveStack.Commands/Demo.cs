using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [ConsoleCommand("upload_demos", "upload demos")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public async void OnUploadDemo(CCSPlayerController? player, CommandInfo command)
    {
        await _gameDemos.UploadDemos();
    }
}
