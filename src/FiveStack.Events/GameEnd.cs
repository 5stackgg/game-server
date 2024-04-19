using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using FiveStack.Enums;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [GameEventHandler]
    public HookResult OnGameEnd(EventCsWinPanelMatch @event, GameEventInfo info)
    {
        _gameDemos.StopDemoRecording();

        MatchManager? match = _matchService.GetCurrentMatch();
        if (match == null)
        {
            return HookResult.Continue;
        }

        Server.NextFrame(async () =>
        {
            await _gameDemos.UploadDemos();
            match.UpdateMapStatus(eMapStatus.Finished);
        });

        return HookResult.Continue;
    }
}
