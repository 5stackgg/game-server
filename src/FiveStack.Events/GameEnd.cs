using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using FiveStack.enums;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [GameEventHandler]
    public HookResult OnGameEnd(EventCsWinPanelMatch @event, GameEventInfo info)
    {
        StopDemoRecording();

        Server.NextFrame(async () =>
        {
            await UploadDemos();
            UpdateMapStatus(eMapStatus.Finished);
        });

        return HookResult.Continue;
    }
}
