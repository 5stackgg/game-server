using System.Text.Json;
using StackExchange.Redis;

namespace PlayCs;

public class Redis
{
    private readonly IDatabase _cache;
    private readonly IDatabase _pubsub;

    public Redis()
    {
        ConnectionMultiplexer redis = ConnectionMultiplexer.Connect("redis");
        _cache = redis.GetDatabase(1);
        _pubsub = redis.GetDatabase(0);
    }

    /**
     * Annoyingly , since guid can be null other places, i have to make it not null here? seems wrong;
     * code smell
     */
    public void PublishMatchEvent<T>(Guid? matchId, EventData<T> eventData)
    {
        if (matchId == null)
        {
            return;
        }

        try
        {
            RedisChannel channel = RedisChannel.Literal($"match:{matchId}");

            _pubsub.Publish(channel, JsonSerializer.Serialize(eventData));
        }
        catch (ArgumentException error)
        {
            Console.WriteLine($"Error: {error.Message}");
        }
    }

    public void PublishServerEvent<T>(string serverId, EventData<T> eventData)
    {
        try
        {
            RedisChannel channel = RedisChannel.Literal($"server:{serverId}");

            _pubsub.Publish(channel, JsonSerializer.Serialize(eventData));
        }
        catch (ArgumentException error)
        {
            Console.WriteLine($"Error: {error.Message}");
        }
    }

    public class EventData<T>
    {
        public string @event { get; set; } = "";
        public T? data { get; set; }
    }
}
