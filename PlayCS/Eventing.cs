using System.Text.Json;
using StackExchange.Redis;

namespace PlayCs;

public class Eventing
{
    private IDatabase? _database;

    public Eventing()
    {
        Init();
    }

    void Init()
    {
        ConnectionMultiplexer redis = ConnectionMultiplexer.Connect("redis");
        _database = redis.GetDatabase(0);
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

        if (_database == null)
        {
            Console.WriteLine("unable to publish, not connected");
            return;
        }
        try
        {
            _database.Publish($"match:{matchId}", JsonSerializer.Serialize(eventData));
        }
        catch (ArgumentException error)
        {
            Console.WriteLine($"Error: {error.Message}");
        }
    }

    public void PublishServerEvent<T>(string serverId, EventData<T> eventData)
    {
        if (_database == null)
        {
            Console.WriteLine("unable to publish, not connected");
            return;
        }
        try
        {
            _database.Publish($"server:{serverId}", JsonSerializer.Serialize(eventData));
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
