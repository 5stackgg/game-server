using System.Net.Http.Headers;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public class GameServer
{
    private int _currentRound = 0;
    private readonly ILogger<GameServer> _logger;
    private readonly EnvironmentService _environmentService;

    public GameServer(ILogger<GameServer> logger, EnvironmentService environmentService)
    {
        _logger = logger;
        _environmentService = environmentService;
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
        return _currentRound;
    }

    public void UpdateCurrentRound()
    {
        _currentRound = MatchUtility.Rules()?.TotalRoundsPlayed ?? 0;
        _logger.LogInformation($"Current Round {_currentRound}");
    }

    public async void Ping()
    {
        string? serverId = _environmentService.GetServerId();
        string? apiPassword = _environmentService.GetServerApiPassword();

        string endpoint = $"{_environmentService.GetApiUrl()}/game-server-node/ping/{serverId}";

        this._logger.LogInformation($"PING: {endpoint}");

        using (HttpClient httpClient = new HttpClient())
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                apiPassword
            );

            try
            {
                HttpResponseMessage response = await httpClient.GetAsync(endpoint);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError($"unable to ping {ex.Message}");
                return;
            }
        }
    }
}
