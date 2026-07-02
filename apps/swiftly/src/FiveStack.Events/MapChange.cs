using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Events;

namespace FiveStack;

public partial class FiveStackPlugin
{
    public async void OnMapLoad(IOnMapLoadEvent @event)
    {
        FiveStack.Utilities.MatchUtility.InvalidateCache();

        _logger.LogInformation("map changed: precaching models");

        await Task.Delay(1000 * 5);
        _core.Scheduler.NextTick(() =>
        {
            MatchManager? match = _matchService.GetCurrentMatch();
            match?.SyncActiveMapAfterMapStart();
            match?.Reset();
            _matchEvents.ClearPendingRoundResult();
            _matchService.GetMatchFromApi();
        });
    }
}
