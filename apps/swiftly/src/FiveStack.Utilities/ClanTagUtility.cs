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

    private const string SetClanSignature = "FiveStack_CCSPlayerController_SetClan";

    private static IUnmanagedFunction<SetClanDelegate>? _setClan;
    private static Guid _hookId;

    public static void Hook(ISwiftlyCore core)
    {
        GamedataSignature? signature = GamedataUtility.Load(core.PluginPath, SetClanSignature);

        nint? address =
            signature == null
                ? null
                : core.Memory.GetAddressBySignature(signature.Library, signature.Pattern);

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
