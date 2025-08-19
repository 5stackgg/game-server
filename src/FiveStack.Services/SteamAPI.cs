using System.Runtime.InteropServices;
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public class SteamAPI
{
    private readonly ILogger<SteamAPI> _logger;
    private IntPtr _gGameServer = IntPtr.Zero;

    [DllImport("steam_api")]
    public static extern IntPtr SteamInternal_CreateInterface(string name);
    
    [DllImport("steam_api", EntryPoint = "SteamGameServer_GetHSteamPipe", CallingConvention = CallingConvention.Cdecl)]
    public static extern int SteamGameServer_GetHSteamPipe();

    [DllImport("steam_api", EntryPoint = "SteamGameServer_GetHSteamUser", CallingConvention = CallingConvention.Cdecl)]
    public static extern int SteamGameServer_GetHSteamUser();
    
    [DllImport("steam_api", EntryPoint = "SteamAPI_ISteamClient_GetISteamGenericInterface", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr ISteamClient_GetISteamGenericInterface(IntPtr instancePtr, IntPtr hSteamUser, IntPtr hSteamPipe, string pchVersion);
    
    [DllImport("steam_api", EntryPoint = "SteamAPI_ISteamGameServer_GetSteamID", CallingConvention = CallingConvention.Cdecl)]
    public static extern ulong ISteamGameServer_GetSteamID(IntPtr instancePtr);
    
    public SteamAPI(ILogger<SteamAPI> logger)
    {
        _logger = logger;
        NativeLibrary.SetDllImportResolver(Assembly.GetExecutingAssembly(), DllImportResolver);
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
        
        var steamClient = SteamInternal_CreateInterface("SteamClient020");
        
        if (steamClient == IntPtr.Zero)
        {
            _logger.LogError("Steam Client failed to load");
            return;
        }
  
        _gGameServer = ISteamClient_GetISteamGenericInterface(steamClient, steamUser, steamPipe, "SteamGameServer014");
        
        if(_gGameServer == IntPtr.Zero)
        {
            _logger.LogError("Failed to get SteamGameServer");
            return;
        }

        _logger.LogInformation("Steam API loaded successfully");
    }
    
    private static IntPtr DllImportResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName == "steam_api")
            return NativeLibrary.Load(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "steam_api64" : "libsteam_api", assembly, searchPath);
        
        return IntPtr.Zero;
    }

    public string GetServerSteamIDFormatted()
    {
        if (_gGameServer != IntPtr.Zero)
        {
            var steamID64 = ISteamGameServer_GetSteamID(_gGameServer);
            if (steamID64 == 0) return "";
            
            return ConvertSteamID64ToSteamID(steamID64);
        }
        return "";
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
            _ => 'I'
        };
        
        return $"[{accountTypeChar}:{universe}:{accountID}:{instance}]";
    }
}
