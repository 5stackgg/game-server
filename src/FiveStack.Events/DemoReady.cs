using System.Runtime.InteropServices;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using FiveStack.Enums;

namespace FiveStack;

public partial class FiveStackPlugin
{
    private static readonly string demoRecordEndSignature = RuntimeInformation.IsOSPlatform(
        OSPlatform.Linux
    )
        ? "55 48 89 E5 41 57 41 56 41 55 41 54 53 48 89 FB 48 81 EC ? ? ? ? 48 8B 7F ? 48 85 FF 0F 84 ? ? ? ?"
        : "";

    public MemoryFunctionVoid<IntPtr, IntPtr> RecordEnd = new(
        demoRecordEndSignature,
        Addresses.EnginePath
    );

    private HookResult RecordEndHookResult(DynamicHook hook)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        if (match == null)
        {
            return HookResult.Continue;
        }

        Server.NextFrame(async () =>
        {
            if (!_environmentService.isOnGameServerNode())
            {
                await _gameDemos.UploadDemos();
            }

            Server.NextFrame(() =>
            {
                if (match.isSurrendered())
                {
                    Guid? winningLineupId = _surrenderSystem.GetWinningLineupId();
                    if (winningLineupId != null)
                    {
                        _matchEvents.PublishGameEvent(
                            "surrender",
                            new Dictionary<string, object>
                            {
                                { "time", DateTime.Now },
                                { "winning_lineup_id", winningLineupId },
                            }
                        );
                    }
                }

                match.UpdateMapStatus(eMapStatus.Finished);
            });
        });
        return HookResult.Continue;
    }
}
