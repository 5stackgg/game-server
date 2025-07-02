using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using FiveStack.Enums;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [GameEventHandler]
    public HookResult OnGameEnd(EventCsWinPanelMatch @event, GameEventInfo info)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        if (match != null)
        {
            match.UpdateMapStatus(eMapStatus.UploadingDemo);
        }

        return HookResult.Continue;
    }
}
