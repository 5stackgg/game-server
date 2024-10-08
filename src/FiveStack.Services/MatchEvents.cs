using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FiveStack.Enums;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public class MatchEvents
{
    private ClientWebSocket? _webSocket;

    private readonly ILogger<MatchEvents> _logger;
    private readonly MatchService _matchService;
    private readonly EnvironmentService _environmentService;

    public MatchEvents(
        ILogger<MatchEvents> logger,
        EnvironmentService environmentService,
        MatchService matchService
    )
    {
        _logger = logger;
        _matchService = matchService;
        _environmentService = environmentService;
        _ = Connect();
    }

    public class EventData<T>
    {
        public string @event { get; set; } = "";
        public Guid matchId { get; set; } = Guid.Empty;
        public T? data { get; set; }
    }

    public void PublishMapStatus(eMapStatus status)
    {
        PublishGameEvent(
            "mapStatus",
            new Dictionary<string, object> { { "status", status.ToString() } }
        );
    }

    public async void PublishGameEvent(string Event, Dictionary<string, object> Data)
    {
        if (IsConnected() == false)
        {
            _logger.LogWarning("not connected to WebSocket");
            return;
        }

        Guid matchId = _matchService.GetCurrentMatch()?.GetMatchData()?.id ?? Guid.Empty;
        if (matchId == Guid.Empty)
        {
            _logger.LogWarning("match data missing");
            return;
        }

        await Publish(
            matchId,
            new MatchEvents.EventData<Dictionary<string, object>>
            {
                data = new Dictionary<string, object> { { "event", Event }, { "data", Data } },
            }
        );
    }

    public async Task<bool> Connect()
    {
        if (_webSocket != null)
        {
            await _webSocket.CloseAsync(
                WebSocketCloseStatus.NormalClosure,
                "Reconnecting",
                CancellationToken.None
            );
        }

        try
        {
            _webSocket = new ClientWebSocket();

            var serverId = _environmentService.GetServerId();
            var serverApiPassword = _environmentService.GetServerApiPassword();

            _webSocket.Options.SetRequestHeader(
                "Authorization",
                $"Basic {Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes($"{serverId}:{serverApiPassword}")
            )}"
            );

            var uri = new Uri($"{_environmentService.GetWsUrl()}/matches");
            await _webSocket.ConnectAsync(uri, CancellationToken.None);

            _logger.LogInformation("Connected to 5stack");

            _matchService.GetMatchFromApi();

            return true;
        }
        catch (WebSocketException ex)
        {
            _logger.LogWarning("Failed to connect to WebSocket server: " + ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError("An error occurred: " + ex.Message);
            return false;
        }
    }

    private Boolean IsConnected()
    {
        return _webSocket?.State == WebSocketState.Open;
    }

    public async Task Disconnect()
    {
        if (_webSocket != null && _webSocket.State == WebSocketState.Open)
        {
            await _webSocket.CloseAsync(
                WebSocketCloseStatus.NormalClosure,
                "Disconnecting",
                CancellationToken.None
            );
            _webSocket = null;
        }
    }

    private async Task Publish<T>(Guid matchId, EventData<T> data)
    {
        if (_webSocket == null || IsConnected() == false)
        {
            _logger.LogWarning("WebSocket is not connected!");
            return;
        }

        data.matchId = matchId;

        try
        {
            var message = JsonSerializer.Serialize(new { @event = "events", data });
            var buffer = Encoding.UTF8.GetBytes(message);
            await _webSocket.SendAsync(
                new ArraySegment<byte>(buffer),
                WebSocketMessageType.Text,
                true,
                CancellationToken.None
            );
        }
        catch (Exception error)
        {
            _logger.LogError($"Error: {error.Message}");
        }
    }
}
