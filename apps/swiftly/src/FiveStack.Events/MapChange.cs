using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.StringTable;

namespace FiveStack;

public partial class FiveStackPlugin
{
    private const string ServerWebsite = "https://5stack.gg";

    public async void OnMapLoad(IOnMapLoadEvent @event)
    {
        FiveStack.Utilities.MatchUtility.InvalidateCache();

        _logger.LogInformation("map changed: precaching models");

        await Task.Delay(1000 * 5);
        FiveStack.Utilities.HibernationUtility.NextTick(() =>
        {
            PublishServerWebsite();
            MatchManager? match = _matchService.GetCurrentMatch();
            match?.SyncActiveMapAfterMapStart();
            match?.Reset();
            _matchEvents.ClearPendingRoundResult();
            _matchService.GetMatchFromApi();
        });
    }

    private void PublishServerWebsite()
    {
        IStringTable? infoPanel = Core.StringTable.FindTable("InfoPanel");
        if (infoPanel == null)
        {
            _logger.LogWarning(
                "InfoPanel string table not found, the scoreboard has no website button"
            );
            return;
        }

        infoPanel.SetOrAddStringUserData("motd", StringTableUserData.FromString(ServerWebsite));
    }
}
