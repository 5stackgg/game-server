using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using FiveStack.Enums;
using FiveStack.Utilities;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [ConsoleCommand("get_match", "Gets match information from api")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void get_match(CCSPlayerController? player, CommandInfo command)
    {
        _matchService.GetMatchFromApi();
    }

    [ConsoleCommand("match_state", "Forces a match to update its current state")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void SetMatchState(CCSPlayerController? player, CommandInfo command)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        eMapStatus status = MatchUtility.MapStatusStringToEnum(command.ArgString);

        if (status == eMapStatus.Warmup)
        {
            string? refusal = RoundRestoreGateUtility.WarmupRefusal(
                match?.GetMatchData() != null,
                match?.GetCurrentMapStatus() ?? eMapStatus.Unknown
            );
            if (refusal != null)
            {
                command.ReplyToCommand(
                    $"{RoundRestoreGateUtility.MatchStateRefusedPrefix} {refusal}"
                );
                return;
            }
        }

        match?.UpdateMapStatus(status);
    }
}
