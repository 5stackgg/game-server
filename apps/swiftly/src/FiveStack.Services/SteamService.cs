using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.SteamAPI;

namespace FiveStack;

public class SteamService
{
    private readonly ILogger<SteamService> _logger;

    public SteamService(ILogger<SteamService> logger)
    {
        _logger = logger;
    }

    public string? GetServerSteamIDFormatted()
    {
        try
        {
            ulong steamID64 = SteamGameServer.GetSteamID().GetSteamID64();

            if (steamID64 == 0)
            {
                return null;
            }

            return SteamIdUtility.ConvertSteamID64ToSteamID(steamID64);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Steam game server not ready for SteamID lookup");
            return null;
        }
    }
}
