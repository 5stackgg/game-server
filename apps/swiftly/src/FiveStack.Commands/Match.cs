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

        _matchService
            .GetCurrentMatch()
            ?.UpdateMapStatus(MatchUtility.MapStatusStringToEnum(string.Join(" ", context.Args)));
    }
}
