using FiveStack.Enums;
using SwiftlyS2.Shared.Commands;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [Command("upload_demos", registerRaw: true, permission: "")]
    public async void OnUploadDemo(ICommandContext context)
    {
        if (context.IsSentByPlayer)
        {
            return;
        }

        MatchManager? match = _matchService.GetCurrentMatch();
        if (match == null)
        {
            return;
        }

        match.UpdateMapStatus(eMapStatus.UploadingDemo);

        await _gameDemos.UploadDemos();
    }

    [Command("test_start_demo", registerRaw: true, permission: "")]
    public void StartDemo(ICommandContext context)
    {
        if (context.IsSentByPlayer)
        {
            return;
        }

        _gameDemos.Start();
    }

    [Command("test_stop_demo", registerRaw: true, permission: "")]
    public void StopDemo(ICommandContext context)
    {
        if (context.IsSentByPlayer)
        {
            return;
        }

        _gameDemos.Stop();
    }
}
