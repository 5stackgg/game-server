using FiveStack.Enums;
using Microsoft.Extensions.Logging;

// using StackExchange.Redis;

namespace FiveStack;

public class MatchEvents
{
    // private IDatabase? _pubsub;
    private readonly ILogger<MatchEvents> _logger;
    private readonly MatchService _matchService;
    private readonly EnvironmentService _environmentService;

    public MatchEvents(
        ILogger<MatchEvents> logger,
        EnvironmentService environmentService,
        MatchService matchService
    )
    {
        //    _pubsub = null;
        _logger = logger;
        _matchService = matchService;
        _environmentService = environmentService;
    }

    public class EventData<T>
    {
        public string @event { get; set; } = "";
        public T? data { get; set; }
    }

    public void PublishMapStatus(eMapStatus status)
    {
        Guid matchId = _matchService.GetCurrentMatch()?.GetMatchData()?.id ?? Guid.Empty;
        if (matchId == Guid.Empty)
        {
            _logger.LogWarning("match data missing");
            return;
        }

        PublishGameEvent(
            matchId,
            "mapStatus",
            new Dictionary<string, object> { { "status", status.ToString() }, }
        );
    }

    public void PublishGameEvent(Guid matchId, string Event, Dictionary<string, object> Data)
    {
        if (IsConnected() == false)
        {
            _logger.LogWarning("not connected to redis");
        }

        Publish(
            $"matches:{matchId}",
            new MatchEvents.EventData<Dictionary<string, object>> { @event = Event, data = Data }
        );
    }

    public bool Connect(string username, string password)
    {
        // if (_pubsub != null)
        // {
        //     Disconnect();
        // }


        // ConfigurationOptions options = new ConfigurationOptions
        // {
        //     EndPoints = { { "redis", 6379 }, },
        //     User = username,
        //     Password = password,
        //     SyncTimeout = 5000,
        //     ConnectTimeout = 5000
        // };

        // ConnectionMultiplexer redis = ConnectionMultiplexer.Connect(options);
        // _pubsub = redis.GetDatabase(0);

        // return IsConnected();
        return false;
    }

    private Boolean IsConnected()
    {
        // Match? match = _matchService.GetMatchData();

        // if (_pubsub == null || match == null)
        // {
        //     return false;
        // }

        // return _pubsub.Multiplexer.IsConnected;
        return false;
    }

    private void Disconnect()
    {
        // if (_pubsub != null)
        // {
        //     _pubsub.Multiplexer.Close();
        //     _pubsub = null;
        // }
    }

    private Boolean Publish<T>(string channel, EventData<T> data)
    {
        // if (_pubsub == null || IsConnected() == false)
        // {
        //     Console.WriteLine("redis is not connected!");
        //     return false;
        // }

        // try
        // {
        //     _pubsub.Publish(RedisChannel.Literal(channel), JsonSerializer.Serialize(data));
        //     return true;
        // }
        // catch (ArgumentException error)
        // {
        //     Console.WriteLine($"Error: {error.Message}");
        // }

        return false;
    }
}
