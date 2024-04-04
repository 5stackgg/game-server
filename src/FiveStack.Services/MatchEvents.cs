using System.Text.Json;
using FiveStack.Enums;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace FiveStack;

// TODO - no idea why but, i have to connect in the constrcutor otherwise it fails.
public class MatchEvents
{
    private readonly IDatabase _pubsub;

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

        ConnectionMultiplexer redis = ConnectionMultiplexer.Connect("redis");
        _pubsub = redis.GetDatabase(0);
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

    //    public bool Connect(string username, string password)
    //     {

    //         try
    //         {
    //             if (_pubsub != null)
    //             {
    //                 Disconnect();
    //             }

    //             // ConfigurationOptions options = new ConfigurationOptions
    //             // {
    //             //     EndPoints = { { "redis", 6379 }, },
    //             //     User = username,
    //             //     Password = password,
    //             // };

    //             ConnectionMultiplexer redis = ConnectionMultiplexer.Connect("redis");
    //             _pubsub = redis.GetDatabase(0);
    //             return true;
    //         }
    //         catch (RedisConnectionException ex)
    //         {
    //             Console.WriteLine("Failed to connect to Redis server: " + ex.Message);
    //             return false;
    //         }
    //         catch (Exception ex)
    //         {
    //             Console.WriteLine("An error occurred: " + ex.Message);
    //             return false;
    //         }
    //     }


    private Boolean IsConnected()
    {
        MatchManager? match = _matchService.GetCurrentMatch();

        if (_pubsub == null || match == null)
        {
            return false;
        }

        return _pubsub.Multiplexer.IsConnected;
    }

    //     public void Disconnect()
    //     {
    //         if (_pubsub != null)
    //         {
    //             _pubsub.Multiplexer.Close();
    //             _pubsub = null;
    //         }
    //     }


    private Boolean Publish<T>(string channel, EventData<T> data)
    {
        if (_pubsub == null || IsConnected() == false)
        {
            Console.WriteLine("redis is not connected!");
            return false;
        }

        try
        {
            _pubsub.Publish(RedisChannel.Literal(channel), JsonSerializer.Serialize(data));
            return true;
        }
        catch (ArgumentException error)
        {
            Console.WriteLine($"Error: {error.Message}");
        }

        return false;
    }
}
