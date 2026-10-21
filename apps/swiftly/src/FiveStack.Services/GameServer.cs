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
    private readonly bool _listening;
    private readonly bool _expected;

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
        _listening = _core.ConVar.Find<bool>(SteamRelayUtility.ListenConVar)?.Value ?? false;
        _expected = SteamRelayUtility.Expected();

        if (_expected && !_listening)
        {
            _logger.LogError(SteamRelayUtility.NotListening);
        }
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

        HibernationUtility.NextTick(() => _core.Engine.ExecuteCommand(string.Join(";", commands)));
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

        _core.Scheduler.NextWorldUpdate(() =>
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
                $"{_environmentService.GetApiUrl()}/game-server-node/ping/{serverId}?map={mapName}&pluginVersion={pluginVersion}&pluginRuntime=swiftlys2&hibernating={(HibernationUtility.IsHibernating ? "true" : "false")}&players={MatchUtility.PlayerCount()}&mapName={Uri.EscapeDataString(_core.Engine.GlobalVars.MapName.ToString())}";

            SteamRelayReport relay = SteamRelayUtility.Report(
                _listening,
                _expected,
                _steamService.GetServerSteamIDFormatted
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

            _ = Task.Run(async () =>
            {
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
        });
    }
}
