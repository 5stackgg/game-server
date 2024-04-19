using System.Text.Json;
using FiveStack.Enums;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace FiveStack;

public class MatchEvents
{
    private IDatabase? _pubsub;
    private ConnectionMultiplexer? connection;

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
        public T? data { get; set; }
    }

    public void PublishMapStatus(eMapStatus status)
    {
        PublishGameEvent(
            "mapStatus",
            new Dictionary<string, object> { { "status", status.ToString() }, }
        );
    }

    public void PublishGameEvent(string Event, Dictionary<string, object> Data)
    {
        if (IsConnected() == false)
        {
            _logger.LogWarning("not connected to redis");
            return;
        }

        Guid matchId = _matchService.GetCurrentMatch()?.GetMatchData()?.id ?? Guid.Empty;
        if (matchId == Guid.Empty)
        {
            _logger.LogWarning("match data missing");
            return;
        }

        Publish(
            $"matches:{matchId}",
            new MatchEvents.EventData<Dictionary<string, object>> { @event = Event, data = Data }
        );
    }

    public async Task<bool> Connect()
    {
        if (connection != null)
        {
            connection.Dispose();
        }

        try
        {
            ConfigurationOptions options = new ConfigurationOptions
            {
                EndPoints = { { "redis", 6379 }, },
                User = _environmentService.GetServerId(),
                Password = _environmentService.GetServerApiPassword(),
            };

            connection = await ConnectionMultiplexer.ConnectAsync(options);
            _pubsub = connection.GetDatabase(0);

            _logger.LogInformation("Connected to Redis");

            _matchService.GetMatchFromApi();

            return true;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning("Failed to connect to Redis server: " + ex.Message);
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
        if (connection == null)
        {
            return false;
        }

        return connection.IsConnected;
    }

    public void Disconnect()
    {
        if (connection != null)
        {
            connection.Close();
            _pubsub = null;
        }
    }

    private Boolean Publish<T>(string channel, EventData<T> data)
    {
        if (_pubsub == null || IsConnected() == false)
        {
            _logger.LogWarning("redis is not connected!");
            return false;
        }

        try
        {
            _pubsub.Publish(RedisChannel.Literal(channel), JsonSerializer.Serialize(data));
            return true;
        }
        catch (ArgumentException error)
        {
            _logger.LogError($"Error: {error.Message}");
        }

        return false;
    }
}
