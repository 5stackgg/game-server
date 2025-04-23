using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FiveStack.Enums;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public class MatchEvents
{
    private bool _isMonitoring = false;
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _connectionCts;
    private bool _manualDisconnect = false;
    private Dictionary<
        Guid,
        (EventData<Dictionary<string, object>> Event, DateTime Timestamp)
    > _pendingMessages = new();
    private System.Timers.Timer _retryTimer;
    private const int RETRY_INTERVAL_MS = 5000;
    private const int MESSAGE_RETRY_THRESHOLD_SECONDS = 10;

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

        _retryTimer = new System.Timers.Timer(RETRY_INTERVAL_MS);
        _retryTimer.Elapsed += async (sender, e) => await RetryPendingMessages();

        _ = ConnectAndMonitor();
    }

    public class EventData<T>
    {
        public string @event { get; set; } = "";
        public Guid matchId { get; set; } = Guid.Empty;
        public Guid messageId { get; set; } = Guid.Empty;
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

    private async Task ConnectAndMonitor()
    {
        _retryTimer.Start();

        if (_isMonitoring || _manualDisconnect)
        {
            return;
        }

        _isMonitoring = true;

        if (!await Connect())
        {
            string? serverId = _environmentService.GetServerId();
            string? serverApiPassword = _environmentService.GetServerApiPassword();

            await Task.Delay(serverId == null || serverApiPassword == null ? 1000 * 10 : 1000 * 3);

            _isMonitoring = false;
            _ = ConnectAndMonitor();
            return;
        }

        _ = MonitorConnection();
    }

    private async Task MonitorConnection()
    {
        var buffer = new byte[38];
        while (_webSocket?.State == WebSocketState.Open)
        {
            try
            {
                var result = await _webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    _connectionCts?.Token ?? CancellationToken.None
                );

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("WebSocket closed, attempting to reconnect");
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var messageIdStr = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    try
                    {
                        var messageId = Guid.Parse(messageIdStr.Trim('"'));
                        _pendingMessages.Remove(messageId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            $"Failed to parse messageId '{messageIdStr}': {ex.Message}"
                        );
                    }
                }
            }
            catch (WebSocketException)
            {
                _logger.LogInformation("WebSocket exception, attempting to reconnect");
                break;
            }
        }

        _isMonitoring = false;

        string? serverId = _environmentService.GetServerId();
        string? serverApiPassword = _environmentService.GetServerApiPassword();

        await Task.Delay(serverId == null || serverApiPassword == null ? 1000 * 10 : 1000 * 3);

        await ConnectAndMonitor();
    }

    public async Task<bool> Connect()
    {
        if (_webSocket != null)
        {
            try
            {
                // Only attempt to close if the WebSocket is in a valid state
                if (
                    _webSocket.State == WebSocketState.Open
                    || _webSocket.State == WebSocketState.CloseReceived
                    || _webSocket.State == WebSocketState.CloseSent
                )
                {
                    await _webSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Reconnecting",
                        CancellationToken.None
                    );
                }
            }
            catch (WebSocketException ex)
            {
                _logger.LogWarning($"Error closing existing WebSocket: {ex.Message}");
            }
            _webSocket = null;
        }

        _connectionCts?.Cancel();
        _connectionCts = new CancellationTokenSource();

        try
        {
            _webSocket = new ClientWebSocket();

            _environmentService.Load();

            string? serverId = _environmentService.GetServerId();
            string? serverApiPassword = _environmentService.GetServerApiPassword();

            if (serverId == null || serverApiPassword == null)
            {
                _logger.LogWarning("Cannot connect to WebSocket, Missing Server ID / API Password");
                return false;
            }

            _webSocket.Options.SetRequestHeader(
                "Authorization",
                $"Basic {Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes($"{serverId}:{serverApiPassword}")
            )}"
            );

            var uri = new Uri($"{_environmentService.GetWsUrl()}/matches");
            await _webSocket.ConnectAsync(uri, _connectionCts.Token);

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

    public async Task Disconnect()
    {
        _manualDisconnect = true;
        _connectionCts?.Cancel();
        _isMonitoring = false;
        _retryTimer.Stop();

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

    private async Task RetryPendingMessages()
    {
        var currentTime = DateTime.UtcNow;

        var messagesToRetry = _pendingMessages
            .Where(m =>
                (currentTime - m.Value.Timestamp).TotalSeconds >= MESSAGE_RETRY_THRESHOLD_SECONDS
            )
            .ToList();

        foreach (var message in messagesToRetry)
        {
            try
            {
                var jsonMessage = JsonSerializer.Serialize(
                    new { @event = "events", data = message.Value.Event }
                );
                var buffer = Encoding.UTF8.GetBytes(jsonMessage);

                if (_webSocket?.State == WebSocketState.Open)
                {
                    await _webSocket.SendAsync(
                        new ArraySegment<byte>(buffer),
                        WebSocketMessageType.Text,
                        true,
                        _connectionCts?.Token ?? CancellationToken.None
                    );

                    _pendingMessages[message.Key] = (message.Value.Event, currentTime);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error retrying message {message.Key}: {ex.Message}");
            }
        }
    }

    private async Task Publish<T>(Guid matchId, EventData<T> data)
    {
        if (_webSocket == null || _webSocket.State == WebSocketState.Closed)
        {
            _logger.LogWarning($"Trying to publish but not connected");
            return;
        }

        data.matchId = matchId;
        data.messageId = Guid.NewGuid();

        if (data is EventData<Dictionary<string, object>> typedData)
        {
            _pendingMessages[data.messageId] = (typedData, DateTime.UtcNow);
        }

        try
        {
            var message = JsonSerializer.Serialize(new { @event = "events", data });
            var buffer = Encoding.UTF8.GetBytes(message);
            await _webSocket!.SendAsync(
                new ArraySegment<byte>(buffer),
                WebSocketMessageType.Text,
                true,
                _connectionCts?.Token ?? CancellationToken.None
            );
        }
        catch (Exception error)
        {
            _logger.LogError($"Error: {error.Message}");
        }
    }
}
