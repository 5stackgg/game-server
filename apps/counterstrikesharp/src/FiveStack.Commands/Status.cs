using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using FiveStack.Entities;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [ConsoleCommand("fivestack_status", "Reports 5Stack plugin and match state")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void FiveStackStatus(CCSPlayerController? player, CommandInfo command)
    {
        command.ReplyToCommand(BuildStatusReport());
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
            $"Plugin Runtime: counterstrikesharp",
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
            lines.Add($"Timeout Active: {_timeoutSystem.IsTimeoutActive()}");
        }

        return string.Join("\n", lines);
    }
}
