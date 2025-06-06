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
    private readonly ILogger<GameServer> _logger;
    private readonly EnvironmentService _environmentService;
    private readonly bool _steamRelay;

    public GameServer(ILogger<GameServer> logger, EnvironmentService environmentService)
    {
        _logger = logger;
        _environmentService = environmentService;
        _steamRelay = ConVar.Find("net_p2p_listen_dedicated")?.GetPrimitiveValue<bool>() ?? false;
    }

    public void SendCommands(string[] commands)
    {
        foreach (var command in commands)
        {
            Server.ExecuteCommand(command);
        }
    }

    public void Message(
        HudDestination destination,
        string message,
        CCSPlayerController? player = null
    )
    {
        if (player != null)
        {
            var parts = message.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
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
            VirtualFunctions.ClientPrintAll(destination, $" {message}", 0, 0, 0, 0);
        }
        else
        {
            var parts = message.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            foreach (var part in parts)
            {
                Server.PrintToChatAll($"{part}");
            }
        }
    }

    public int GetCurrentRound()
    {
        return MatchUtility.Rules()?.TotalRoundsPlayed ?? 0;
    }

    public void Ping(string pluginVersion)
    {
        string? serverId = _environmentService.GetServerId();
        string? apiPassword = _environmentService.GetServerApiPassword();

        Server.NextFrame(async () =>
        {
            string endpoint =
                $"{_environmentService.GetApiUrl()}/game-server-node/ping/{serverId}?map={Server.MapName}&pluginVersion={pluginVersion}&steamRelay={_steamRelay}";

            using (HttpClient httpClient = new HttpClient())
            {
                httpClient.Timeout = TimeSpan.FromSeconds(5);
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    apiPassword
                );

                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    HttpResponseMessage response = await httpClient.GetAsync(endpoint, cts.Token);
                    response.EnsureSuccessStatusCode();
                }
                catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
                {
                    _logger.LogWarning("Ping request timed out after 5 seconds");
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogError($"Unable to ping: {ex.Message}");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Unexpected error during ping: {ex.Message}");
                }
            }
        });
    }
}
