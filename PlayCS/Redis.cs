using System.Text.Json;
using PlayCs.entities;
using StackExchange.Redis;

namespace PlayCs;

public class Redis
{
    private IDatabase cache;
    private IDatabase pubsub;

    public Redis()
    {
        ConnectionMultiplexer redis = ConnectionMultiplexer.Connect("redis");
        pubsub = redis.GetDatabase(0);
        cache = redis.GetDatabase(1);
    }

    public Match? getMatch(string matchId)
    {
        string? match = cache.StringGet($"match:{matchId}");

        if (match == null)
        {
            return null;
        }

        return JsonSerializer.Deserialize<Match>(match);
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

        if (pubsub == null)
        {
            Console.WriteLine("unable to publish, not connected");
            return;
        }
        try
        {
            pubsub.Publish($"match:{matchId}", JsonSerializer.Serialize(eventData));
        }
        catch (ArgumentException error)
        {
            Console.WriteLine($"Error: {error.Message}");
        }
    }

    public void PublishServerEvent<T>(string serverId, EventData<T> eventData)
    {
        if (pubsub == null)
        {
            Console.WriteLine("unable to publish, not connected");
            return;
        }
        try
        {
            pubsub.Publish($"server:{serverId}", JsonSerializer.Serialize(eventData));
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
