using System.Text.Json;
using StackExchange.Redis;

namespace PlayCs;

public class Redis
{
    private IDatabase? connection;

    public Redis()
    {
        Init();
    }

    void Init()
    {
        ConnectionMultiplexer redis = ConnectionMultiplexer.Connect("redis");
        connection = redis.GetDatabase(0);
    }

    public void Publish<T>(string matchId, string eventName, EventData<T> eventData)
    {
        try
        {
            connection.Publish($"{eventName}:{matchId}", JsonSerializer.Serialize(eventData));
        }
        catch (ArgumentException error)
        {
            Console.WriteLine($"Error: {error.Message}");
        }
    }

    public class EventData<T>
    {
        public string @event { get; set; }
        public T data { get; set; }
    }
}
