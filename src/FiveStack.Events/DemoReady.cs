using System.Runtime.InteropServices;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using FiveStack.Enums;

namespace FiveStack;

public partial class FiveStackPlugin
{
    private static string g_sDemosDir = Server.GameDirectory;

    private static readonly string g_BinaryPath =
            g_sDemosDir
            + "/bin/"
            + (
                RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                    ? "linuxsteamrt64/libengine2.so"
                    : "win64/engine2.dll"
            ),
        g_Signature = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? @"\x55\x48\x89\xE5\x41\x57\x41\x56\x41\x55\x49\x89\xF5\x41\x54\x4C\x8D\x67\x08"
            : @"\x40\x55\x56\x41\x57\x48\x8D\x6C\x24\x00\x48\x81\xEC\x00\x00\x00\x00\x80\xB9\x00\x00\x00\x00\x00";

    public MemoryFunctionVoid<IntPtr, IntPtr> RecordEnd = new(g_Signature, g_BinaryPath);

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
                match.UpdateMapStatus(eMapStatus.Finished);
            });
        });
        return HookResult.Continue;
    }
}
