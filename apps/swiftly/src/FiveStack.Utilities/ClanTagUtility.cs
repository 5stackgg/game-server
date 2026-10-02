using System.Runtime.InteropServices;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Memory;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace FiveStack.Utilities;

// void CCSPlayerController::SetClan(uint32 clanId, const char* tag)
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void SetClanDelegate(nint controller, uint clanId, nint tag);

// CS2 reapplies the player's Steam group tag through SetClan every time their GC
// persona data refreshes, which overwrites our tag and makes the scoreboard flicker.
// The hook drops every game call, and ours go to the original past the hook.
public static class ClanTagUtility
{
    // any nonzero id; the client only shows the tag when one is set
    private const uint CustomClanId = 1;

    private static readonly string SetClanSignature = RuntimeInformation.IsOSPlatform(
        OSPlatform.Linux
    )
        ? "55 48 89 E5 41 57 41 56 41 55 49 89 D5 41 54 53 48 89 FB 48 81 EC ? ? ? ? 39 B7 ? ? ? ?"
        : "48 89 5C 24 10 48 89 6C 24 18 48 89 74 24 20 57 48 83 EC 20 49 8B E8 8B FA 48 8B F1 39 91 ? ? ? ? 74 ? BA FF FF FF FF";

    private static IUnmanagedFunction<SetClanDelegate>? _setClan;
    private static Guid _hookId;

    public static void Hook(ISwiftlyCore core)
    {
        nint? address = core.Memory.GetAddressBySignature(Library.Server, SetClanSignature);
        if (address == null || address == nint.Zero)
        {
            throw new InvalidOperationException("SetClan signature not found");
        }

        _setClan = core.Memory.GetUnmanagedFunctionByAddress<SetClanDelegate>(address.Value);
        _hookId = _setClan.AddHook(next => (controller, clanId, tag) => { });
    }

    public static void Unhook()
    {
        if (_setClan != null && _hookId != Guid.Empty)
        {
            _setClan.RemoveHook(_hookId);
        }

        _setClan = null;
        _hookId = Guid.Empty;
    }

    public static void Set(CCSPlayerController controller, string tag)
    {
        uint clanId = tag == "" ? 0 : CustomClanId;

        // SetClan sends a network update on every change
        if (controller.ClanId32bit == clanId && (controller.Clan ?? "") == tag)
        {
            return;
        }

        if (_setClan == null)
        {
            controller.ClanId32bit = clanId;
            controller.ClanId32bitUpdated();
            controller.Clan = tag;
            controller.ClanUpdated();
            return;
        }

        nint tagPtr = Marshal.StringToCoTaskMemUTF8(tag);
        try
        {
            _setClan.CallOriginal(controller.Address, clanId, tagPtr);
        }
        finally
        {
            Marshal.FreeCoTaskMem(tagPtr);
        }
    }
}
