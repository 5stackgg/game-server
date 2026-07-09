using System.Net.Http.Headers;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace FiveStack;

public class GameServer
{
    private readonly ISwiftlyCore _core;
    private readonly SteamService _steamService;
    private readonly MatchService _matchService;
    private readonly EnvironmentService _environmentService;
    private readonly ILogger<GameServer> _logger;
    private readonly bool _steamRelay;

    public GameServer(
        ISwiftlyCore core,
        ILogger<GameServer> logger,
        SteamService steamService,
        EnvironmentService environmentService,
        MatchService matchService
    )
    {
        _core = core;
        _logger = logger;
        _steamService = steamService;
        _matchService = matchService;
        _environmentService = environmentService;
        _steamRelay = _core.ConVar.Find<bool>("net_p2p_listen_dedicated")?.Value ?? false;
    }

    public void SendCommands(string[] commands)
    {
        // Trace who is triggering a game restart — it should not happen on reload.
        if (commands.Any(command => command.Contains("mp_restartgame")))
        {
            _logger.LogWarning(
                "SendCommands issuing mp_restartgame: [{Commands}]\n{StackTrace}",
                string.Join(";", commands),
                Environment.StackTrace
            );
        }

        _core.Scheduler.NextTick(() => _core.Engine.ExecuteCommand(string.Join(";", commands)));
    }

    public void Message(
        MessageType destination,
        string message,
        IPlayer? player = null
    )
    {
        // SwiftlyS2 only auto-applies .Colored() to translation files at load time;
        // strings built in code (e.g. with ChatColors.*) reach SendChat as literal
        // "[red]" placeholders unless we convert them here.
        message = message.Colored();

        if (player != null)
        {
            var parts = message.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
            foreach (var part in parts)
            {
                player.SendChat(part);
            }
        }
        else if (destination == MessageType.Console)
        {
            _core.ConsoleOutput.WriteToServerConsole(message);
        }
        else if (destination == MessageType.Alert)
        {
            _core.PlayerManager.SendAlert($" {message}");
        }
        else if (destination == MessageType.Center)
        {
            _core.PlayerManager.SendCenter($" {message}");
        }
        else
        {
            var parts = message.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
            foreach (var part in parts)
            {
                _core.PlayerManager.SendChat(part);
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

        _core.Scheduler.NextTick(() =>
        {
            if (serverId == null || apiPassword == null)
            {
                _logger.LogCritical("Server ID or API password is null");
                return;
            }

            string? workshopID = _matchService.GetWorkshopID();

            string mapName = string.IsNullOrEmpty(workshopID)
                ? _core.Engine.GlobalVars.MapName.ToString()
                : workshopID;

            string endpoint =
                $"{_environmentService.GetApiUrl()}/game-server-node/ping/{serverId}?map={mapName}&pluginVersion={pluginVersion}&pluginRuntime=swiftlys2";

            if (_steamRelay)
            {
                endpoint += $"&steamRelay={_steamRelay}";
                string? serverSteamID = _steamService.GetServerSteamIDFormatted();

                if (serverSteamID == null)
                {
                    _logger.LogInformation("still connecting to the steam relay");
                    return;
                }

                endpoint += $"&steamID={serverSteamID}";
            }

            _ = Task.Run(async () =>
            {
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
                        HttpResponseMessage response = await httpClient.GetAsync(
                            endpoint,
                            cts.Token
                        );
                        response.EnsureSuccessStatusCode();
                    }
                    catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
                    {
                        _logger.LogWarning("Ping request timed out after 5 seconds");
                    }
                    catch (HttpRequestException ex)
                    {
                        _logger.LogCritical($"Unable to ping: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogCritical($"Unexpected error during ping: {ex.Message}");
                    }
                }
            });
        });
    }
}
