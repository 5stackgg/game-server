using System.Runtime.InteropServices;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;

namespace FiveStack.Utilities;

// CS2 reapplies the player's Steam group tag through SetClan every time their GC
// persona data refreshes, which overwrites our tag and makes the scoreboard flicker.
// The hook drops every game call, and ours go through with the hook bypassed.
public static class ClanTagUtility
{
    // any nonzero id; the client only shows the tag when one is set
    private const uint CustomClanId = 1;

    private static readonly string SetClanSignature = RuntimeInformation.IsOSPlatform(
        OSPlatform.Linux
    )
        ? "55 48 89 E5 41 57 41 56 41 55 49 89 D5 41 54 53 48 89 FB 48 81 EC ? ? ? ? 39 B7 ? ? ? ?"
        : "48 89 5C 24 10 48 89 6C 24 18 48 89 74 24 20 57 48 83 EC 20 49 8B E8 8B FA 48 8B F1 39 91 ? ? ? ? 74 ? BA FF FF FF FF";

    // void CCSPlayerController::SetClan(uint32 clanId, const char* tag)
    private static MemoryFunctionVoid<CCSPlayerController, uint, string>? _setClan;
    private static bool _settingClan;

    public static void Hook()
    {
        _setClan = new(SetClanSignature, Addresses.ServerPath);
        _setClan.Hook(OnSetClan, HookMode.Pre);
    }

    public static void Unhook()
    {
        _setClan?.Unhook(OnSetClan, HookMode.Pre);
        _setClan = null;
    }

    private static HookResult OnSetClan(DynamicHook hook)
    {
        return _settingClan ? HookResult.Continue : HookResult.Handled;
    }

    public static void Set(CCSPlayerController player, string tag)
    {
        uint clanId = tag == "" ? 0 : CustomClanId;

        // SetClan sends a network update on every change
        if (player.ClanId32bit == clanId && (player.Clan ?? "") == tag)
        {
            return;
        }

        if (_setClan == null)
        {
            player.ClanId32bit = clanId;
            player.Clan = tag;
            CounterStrikeSharp.API.Utilities.SetStateChanged(
                player,
                "CCSPlayerController",
                "m_unClanId32bit"
            );
            CounterStrikeSharp.API.Utilities.SetStateChanged(
                player,
                "CCSPlayerController",
                "m_szClan"
            );
            return;
        }

        _settingClan = true;
        try
        {
            _setClan.Invoke(player, clanId, tag);
        }
        finally
        {
            _settingClan = false;
        }
    }
}
