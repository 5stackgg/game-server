using FiveStack.Entities;
using SwiftlyS2.Shared.Commands;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [Command("fivestack_status", registerRaw: true, permission: "")]
    public void FiveStackStatus(ICommandContext context)
    {
        if (context.IsSentByPlayer)
        {
            return;
        }

        // Reply() (not the logger) is what reaches RCON: SwiftlyS2 routes its
        // logger to its own sink, while Reply from console goes through Msg().
        context.Reply(BuildStatusReport());
    }

    private string BuildStatusReport()
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();
        MatchMap? currentMap = match?.GetCurrentMap();

        List<string> lines =
        [
            "----- 5Stack -----",
            $"Plugin Version: {ModuleVersion}",
            $"Plugin Runtime: swiftlys2",
            $"Server ID: {_environmentService.GetServerId() ?? "unassigned"}",
            $"API: {_environmentService.GetApiUrl()}",
            $"API Socket: {(_environmentService.IsOfflineMode() ? "offline mode" : _matchEvents.IsConnected() ? "connected" : "disconnected")}",
        ];

        if (matchData == null)
        {
            lines.Add("Match: none");
        }
        else
        {
            lines.Add($"Match: {matchData.id}");
            lines.Add($"Map: {currentMap?.map.name ?? "unknown"}");
            lines.Add($"Map Status: {match!.GetCurrentMapStatus()}");
            lines.Add($"Timeout Active: {match.timeoutSystem.IsTimeoutActive()}");
        }

        return string.Join("\n", lines);
    }
}
