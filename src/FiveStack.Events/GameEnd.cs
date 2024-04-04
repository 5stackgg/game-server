using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using FiveStack.Entities;
using FiveStack.Enums;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [GameEventHandler]
    public HookResult OnGameEnd(EventCsWinPanelMatch @event, GameEventInfo info)
    {
        _matchDemos.StopDemoRecording();

        FiveStackMatch? match = _matchService.GetCurrentMatchData();
        if (match == null)
        {
            return HookResult.Continue;
        }

        Server.NextFrame(async () =>
        {
            await _matchDemos.UploadDemos(match);
            _matchService.UpdateMapStatus(eMapStatus.Finished);
        });

        return HookResult.Continue;
    }
}
