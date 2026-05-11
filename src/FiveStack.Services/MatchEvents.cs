using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Utilities;
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
    private readonly GameServer _gameServer;

    public MatchEvents(
        ILogger<MatchEvents> logger,
        EnvironmentService environmentService,
        MatchService matchService,
        GameServer gameServer
    )
    {
        _logger = logger;
        _matchService = matchService;
        _environmentService = environmentService;
        _gameServer = gameServer;

        _retryTimer = new System.Timers.Timer(RETRY_INTERVAL_MS);
        _retryTimer.Elapsed += async (sender, e) => await RetryPendingMessages();

        _ = ConnectAndMonitor();
    }

    public class EventData<T>
    {
        public string @event { get; set; } = "";
        public Guid matchId { get; set; } = Guid.Empty;
        public Guid mapId { get; set; } = Guid.Empty;
        public Guid messageId { get; set; } = Guid.Empty;
        public T? data { get; set; }
    }

    public void PublishMapStatus(eMapStatus status, Guid? winningLineupId)
    {
        if (_environmentService.IsOfflineMode())
        {
            return;
        }
        _logger.LogInformation($"PublishMapStatus status={status} winning_lineup_id={winningLineupId?.ToString() ?? "<null>"}");
        PublishGameEvent(
            "mapStatus",
            new Dictionary<string, object>
            {
                { "status", status.ToString() },
                { "winning_lineup_id", winningLineupId?.ToString() ?? "" },
            }
        );
    }

    public async void PublishGameEvent(string Event, Dictionary<string, object> Data)
    {
        if (_environmentService.IsOfflineMode())
        {
            return;
        }

        Guid matchId = _matchService.GetCurrentMatch()?.GetMatchData()?.id ?? Guid.Empty;
        if (matchId == Guid.Empty)
        {
            return;
        }

        Guid mapId = _matchService.GetCurrentMatch()?.GetActiveMapId() ?? Guid.Empty;
        if (mapId == Guid.Empty)
        {
            return;
        }

        await Publish(
            matchId,
            mapId,
            new EventData<Dictionary<string, object>>
            {
                data = new Dictionary<string, object> { { "event", Event }, { "data", Data } },
            }
        );
    }

    private async Task ConnectAndMonitor()
    {
        if (_environmentService.IsOfflineMode())
        {
            return;
        }

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
            _webSocket.Dispose();
            _webSocket = null;
        }

        _connectionCts?.Cancel();
        _connectionCts = new CancellationTokenSource();

        ClientWebSocket? newWebSocket = null;
        try
        {
            newWebSocket = new ClientWebSocket();

            _environmentService.Load();

            string? serverId = _environmentService.GetServerId();
            string? serverApiPassword = _environmentService.GetServerApiPassword();

            if (serverId == null || serverApiPassword == null)
            {
                _logger.LogWarning("Cannot connect to WebSocket, Missing Server ID / API Password");
                newWebSocket.Dispose();
                return false;
            }

            newWebSocket.Options.SetRequestHeader(
                "Authorization",
                $"Basic {Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes($"{serverId}:{serverApiPassword}")
            )}"
            );

            var uri = new Uri($"{_environmentService.GetWsUrl()}/matches");
            await newWebSocket.ConnectAsync(uri, _connectionCts.Token);

            _webSocket = newWebSocket;

            _matchService.GetMatchFromApi();

            return true;
        }
        catch (WebSocketException ex)
        {
            _logger.LogWarning("Failed to connect to WebSocket server: " + ex.Message);
            newWebSocket?.Dispose();
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogCritical("An error occurred: " + ex.Message);
            newWebSocket?.Dispose();
            return false;
        }
    }

    public async Task Disconnect()
    {
        _manualDisconnect = true;
        _connectionCts?.Cancel();
        _isMonitoring = false;
        _retryTimer.Stop();
        _retryTimer?.Dispose();

        if (_webSocket != null && _webSocket.State == WebSocketState.Open)
        {
            await _webSocket.CloseAsync(
                WebSocketCloseStatus.NormalClosure,
                "Disconnecting",
                CancellationToken.None
            );
            _webSocket.Dispose();
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

    private async Task Publish<T>(Guid matchId, Guid mapId, EventData<T> data)
    {
        if (_webSocket == null || _webSocket.State == WebSocketState.Closed)
        {
            _logger.LogWarning($"Trying to publish but not connected");
            return;
        }

        data.mapId = mapId;
        data.matchId = matchId;
        data.messageId = Guid.NewGuid();

        if (data is EventData<Dictionary<string, object>> typedData)
        {
            _pendingMessages[data.messageId] = (typedData, DateTime.UtcNow);

            if (_pendingMessages.Count > 1000)
            {
                var oldest = _pendingMessages.OrderBy(p => p.Value.Timestamp).First();
                _pendingMessages.Remove(oldest.Key);
            }
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
            _logger.LogCritical($"Error: {error.Message}");
        }
    }

    public (
        int lineup1Score,
        int lineup2Score,
        CsTeam lineup1Side,
        CsTeam lineup2Side,
        int totalRoundsPlayed
    ) GetRoundInformation()
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();
        MatchMap? currentMap = match?.GetCurrentMap();

        if (match == null || matchData == null || currentMap == null)
        {
            _logger.LogWarning($"GetRoundInformation: null state, returning zeros (match={match == null}, matchData={matchData == null}, currentMap={currentMap == null})");
            return (0, 0, CsTeam.None, CsTeam.None, 0);
        }

        bool rulesNull = MatchUtility.Rules() == null;
        int totalRoundsPlayed = _gameServer.GetTotalRoundsPlayed();
        int recordedRoundIndex = Math.Max(0, totalRoundsPlayed - 1);

        CsTeam lineup1Side = TeamUtility.GetLineupSide(
            matchData,
            currentMap,
            matchData.lineup_1_id,
            recordedRoundIndex
        );
        CsTeam lineup2Side = TeamUtility.GetLineupSide(
            matchData,
            currentMap,
            matchData.lineup_2_id,
            recordedRoundIndex
        );

        int lineup1Score = TeamUtility.GetTeamScore(
            matchData,
            currentMap,
            matchData.lineup_1_id,
            recordedRoundIndex
        );
        int lineup2Score = TeamUtility.GetTeamScore(
            matchData,
            currentMap,
            matchData.lineup_2_id,
            recordedRoundIndex
        );

        int liveTScore = 0;
        int liveCtScore = 0;
        foreach (var team in MatchUtility.Teams())
        {
            if (team.TeamNum == (int)CsTeam.Terrorist) liveTScore = team.Score;
            else if (team.TeamNum == (int)CsTeam.CounterTerrorist) liveCtScore = team.Score;
        }

        _logger.LogInformation($"GetRoundInformation match={matchData.id} map={currentMap.id} active_map={match.GetActiveMapId()} gameEnded={match.gameEnded} rules_null={rulesNull} totalRoundsPlayed={totalRoundsPlayed} recordedRoundIndex={recordedRoundIndex} mr={matchData.options.mr} map_l1_side={currentMap.lineup_1_side} map_l2_side={currentMap.lineup_2_side} l1_side={lineup1Side} l1_score={lineup1Score} l2_side={lineup2Side} l2_score={lineup2Score} live_t={liveTScore} live_ct={liveCtScore}");

        return (lineup1Score, lineup2Score, lineup1Side, lineup2Side, totalRoundsPlayed);
    }

    public Guid? GetWinningLineupId()
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();
        MatchMap? currentMap = match?.GetCurrentMap();

        if (match == null || matchData == null || currentMap == null)
        {
            _logger.LogWarning($"GetWinningLineupId: null state, returning null (match={match == null}, matchData={matchData == null}, currentMap={currentMap == null})");
            return null;
        }

        (
            int lineup1Score,
            int lineup2Score,
            CsTeam lineup1Side,
            CsTeam lineup2Side,
            int totalRoundsPlayed
        ) = GetRoundInformation();

        Guid? winnerId =
            lineup1Score > lineup2Score ? matchData.lineup_1_id : matchData.lineup_2_id;

        string winnerLabel;
        if (lineup1Score == lineup2Score)
        {
            winnerLabel = "TIE_DEFAULT_LINEUP_2";
        }
        else if (lineup1Score > lineup2Score)
        {
            winnerLabel = "LINEUP_1";
        }
        else
        {
            winnerLabel = "LINEUP_2";
        }

        int liveTScore = 0;
        int liveCtScore = 0;
        foreach (var team in MatchUtility.Teams())
        {
            if (team.TeamNum == (int)CsTeam.Terrorist)
            {
                liveTScore = team.Score;
            }
            else if (team.TeamNum == (int)CsTeam.CounterTerrorist)
            {
                liveCtScore = team.Score;
            }
        }

        _logger.LogInformation($"GetWinningLineupId match={matchData.id} map={currentMap.id} mr={matchData.options.mr} totalRoundsPlayed={totalRoundsPlayed} l1_id={matchData.lineup_1_id} l1_starting_side={currentMap.lineup_1_side} l1_resolved_side={lineup1Side} l1_score={lineup1Score} l2_id={matchData.lineup_2_id} l2_starting_side={currentMap.lineup_2_side} l2_resolved_side={lineup2Side} l2_score={lineup2Score} live_t={liveTScore} live_ct={liveCtScore} decision={winnerLabel} winner={winnerId}");

        return winnerId;
    }
}
