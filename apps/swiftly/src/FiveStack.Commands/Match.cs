using FiveStack.Enums;
using FiveStack.Utilities;
using SwiftlyS2.Shared.Commands;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [Command("get_match", registerRaw: true, permission: "")]
    public void get_match(ICommandContext context)
    {
        if (context.IsSentByPlayer)
        {
            return;
        }

        _matchService.GetMatchFromApi();
    }

    [Command("match_state", registerRaw: true, permission: "")]
    public void SetMatchState(ICommandContext context)
    {
        if (context.IsSentByPlayer)
        {
            return;
        }

        MatchManager? match = _matchService.GetCurrentMatch();
        eMapStatus status = MatchUtility.MapStatusStringToEnum(string.Join(" ", context.Args));

        if (status == eMapStatus.Warmup)
        {
            string? refusal = RoundRestoreGateUtility.WarmupRefusal(
                match?.GetMatchData() != null,
                match?.GetCurrentMapStatus() ?? eMapStatus.Unknown
            );
            if (refusal != null)
            {
                context.Reply($"{RoundRestoreGateUtility.MatchStateRefusedPrefix} {refusal}");
                return;
            }
        }

        match?.UpdateMapStatus(status);
    }
}
