using System.Net.Http.Headers;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public class GameServer
{
    private readonly SteamAPI _steamAPI;
    private readonly MatchService _matchService;
    private readonly EnvironmentService _environmentService;
    private readonly ILogger<GameServer> _logger;
    private readonly bool _listening;
    private readonly bool _expected;

    public GameServer(
        ILogger<GameServer> logger,
        SteamAPI steamAPI,
        EnvironmentService environmentService,
        MatchService matchService
    )
    {
        _logger = logger;
        _steamAPI = steamAPI;
        _matchService = matchService;
        _environmentService = environmentService;
        _listening =
            ConVar.Find(SteamRelayUtility.ListenConVar)?.GetPrimitiveValue<bool>() ?? false;
        _expected = SteamRelayUtility.Expected();

        if (_expected && !_listening)
        {
            _logger.LogError(SteamRelayUtility.NotListening);
        }
    }

    public void SendCommands(string[] commands)
    {
        HibernationUtility.NextFrame(() => Server.ExecuteCommand(string.Join(";", commands)));
    }

    public void Message(
        HudDestination destination,
        string message,
        CCSPlayerController? player = null
    )
    {
        if (player != null)
        {
            var parts = message.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
            foreach (var part in parts)
            {
                player.PrintToChat($"{part}");
            }
        }
        else if (destination == HudDestination.Console)
        {
            Server.PrintToConsole(message);
        }
        else if (destination == HudDestination.Alert || destination == HudDestination.Center)
        {
            VirtualFunctions.ClientPrintAll(destination, $" {message}", 0, 0, 0, 0, 0);
        }
        else
        {
            var parts = message.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
            foreach (var part in parts)
            {
                Server.PrintToChatAll($"{part}");
            }
        }
    }

    public int GetCurrentRound()
    {
        return GetTotalRoundsPlayed() + 1;
    }

    public int GetTotalRoundsPlayed()
    {
        return MatchUtility.Rules()?.TotalRoundsPlayed ?? 0;
    }

    public void Ping(string pluginVersion)
    {
        if (_environmentService.IsOfflineMode())
        {
            return;
        }

        string? serverId = _environmentService.GetServerId();
        string? apiPassword = _environmentService.GetServerApiPassword();

        Server.NextWorldUpdate(async () =>
        {
            if (serverId == null || apiPassword == null)
            {
                _logger.LogCritical("Server ID or API password is null");
                return;
            }

            string? workshopID = _matchService.GetWorkshopID();

            string endpoint =
                $"{_environmentService.GetApiUrl()}/game-server-node/ping/{serverId}?map={(string.IsNullOrEmpty(workshopID) ? Server.MapName : workshopID)}&pluginVersion={pluginVersion}&pluginRuntime=counterstrikesharp&hibernating={(HibernationUtility.IsHibernating ? "true" : "false")}&players={MatchUtility.Players().Count}&mapName={Uri.EscapeDataString(Server.MapName)}";

            SteamRelayReport relay = SteamRelayUtility.Report(
                _listening,
                _expected,
                _steamAPI.GetServerSteamIDFormatted
            );

            // This ping is what tells the panel the server is up.
            if (relay.Pending)
            {
                _logger.LogInformation("still connecting to the steam relay");
                return;
            }

            if (relay.OnRelay)
            {
                endpoint += $"&steamRelay=true&steamID={relay.Address}";
            }

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    apiPassword
                );
                HttpResponseMessage response = await HttpClientProvider.Client.SendAsync(
                    request,
                    cts.Token
                );
                response.EnsureSuccessStatusCode();
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Ping request timed out after 5 seconds");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogCritical($"Unable to ping: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Unexpected error during ping");
            }
        });
    }
}
