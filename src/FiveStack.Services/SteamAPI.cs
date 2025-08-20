using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public class SteamAPI
{
    private IntPtr _gGameServer = IntPtr.Zero;
    private readonly ILogger<SteamAPI> _logger;

    [DllImport("steam_api")]
    private static extern IntPtr SteamInternal_CreateInterface(string name);

    [DllImport(
        "steam_api",
        EntryPoint = "SteamGameServer_GetHSteamPipe",
        CallingConvention = CallingConvention.Cdecl
    )]
    private static extern int SteamGameServer_GetHSteamPipe();

    [DllImport(
        "steam_api",
        EntryPoint = "SteamGameServer_GetHSteamUser",
        CallingConvention = CallingConvention.Cdecl
    )]
    private static extern int SteamGameServer_GetHSteamUser();

    [DllImport(
        "steam_api",
        EntryPoint = "SteamAPI_ISteamClient_GetISteamGenericInterface",
        CallingConvention = CallingConvention.Cdecl
    )]
    private static extern IntPtr ISteamClient_GetISteamGenericInterface(
        IntPtr instancePtr,
        IntPtr hSteamUser,
        IntPtr hSteamPipe,
        string pchVersion
    );

    [DllImport(
        "steam_api",
        EntryPoint = "SteamAPI_ISteamGameServer_GetSteamID",
        CallingConvention = CallingConvention.Cdecl
    )]
    private static extern ulong ISteamGameServer_GetSteamID(IntPtr instancePtr);

    public SteamAPI(ILogger<SteamAPI> logger)
    {
        _logger = logger;
        NativeLibrary.SetDllImportResolver(Assembly.GetExecutingAssembly(), DllImportResolver);
    }

    public string? GetServerSteamIDFormatted()
    {
        if (_gGameServer != IntPtr.Zero)
        {
            var steamID64 = ISteamGameServer_GetSteamID(_gGameServer);
            if (steamID64 == 0)
            {
                return null;
            }

            return ConvertSteamID64ToSteamID(steamID64);
        }
        return null;
    }

    public void OnSteamAPIActivated()
    {
        LoadSteamClient();
    }

    public void OnSteamAPIDeactivated()
    {
        _gGameServer = IntPtr.Zero;
    }

    private void LoadSteamClient()
    {
        var steamPipe = SteamGameServer_GetHSteamPipe();
        var steamUser = SteamGameServer_GetHSteamUser();

        if (steamPipe == 0 || steamUser == 0)
        {
            _logger.LogError("Steam API failed to load");
            return;
        }

        // See: https://github.com/ValveSoftware/Proton/tree/proton_10.0/lsteamclient
        // You can find SteamClient and SteamGameServer interface versions there.
        // It's unclear how to determine which version to use.

        var steamClient = SteamInternal_CreateInterface("SteamClient020");

        if (steamClient == IntPtr.Zero)
        {
            _logger.LogError("Steam Client failed to load");
            return;
        }

        _gGameServer = ISteamClient_GetISteamGenericInterface(
            steamClient,
            steamUser,
            steamPipe,
            "SteamGameServer014"
        );

        if (_gGameServer == IntPtr.Zero)
        {
            _logger.LogError("Failed to get SteamGameServer");
            return;
        }

        _logger.LogInformation("Steam API loaded successfully");
    }

    private static IntPtr DllImportResolver(
        string libraryName,
        Assembly assembly,
        DllImportSearchPath? searchPath
    )
    {
        if (libraryName == "steam_api")
            return NativeLibrary.Load(
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? "steam_api64"
                    : "libsteam_api",
                assembly,
                searchPath
            );

        return IntPtr.Zero;
    }

    private string ConvertSteamID64ToSteamID(ulong steamID64)
    {
        // https://developer.valvesoftware.com/wiki/SteamID
        // Bits 0-31: Account ID
        // Bits 32-51: Instance (20 bits)
        // Bits 52-55: Account Type (4 bits)
        // Bits 56-63: Universe (8 bits)

        uint accountID = (uint)(steamID64 & 0xFFFFFFFF);
        uint instance = (uint)((steamID64 >> 32) & 0xFFFFF);
        uint accountType = (uint)((steamID64 >> 52) & 0xF);
        uint universe = (uint)((steamID64 >> 56) & 0xFF);

        char accountTypeChar = accountType switch
        {
            0 => 'I', // Invalid
            1 => 'U', // Individual
            2 => 'M', // Multiseat
            3 => 'G', // GameServer
            4 => 'A', // AnonGameServer
            5 => 'P', // Pending
            6 => 'C', // ContentServer
            7 => 'g', // Clan
            8 => 'T', // Chat
            _ => 'I',
        };

        return $"[{accountTypeChar}:{universe}:{accountID}:{instance}]";
    }
}
