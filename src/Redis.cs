using System.Text.Json;
using System.Threading.Channels;
using CounterStrikeSharp.API.Core;
using StackExchange.Redis;

namespace FiveStack;

public class Redis
{
    private IDatabase? _pubsub;

    public Redis()
    {
        _pubsub = null;
    }

    public void Connect(string username, string password)
    {
        if (_pubsub != null)
        {
            Disconnect();
        }

        ConfigurationOptions options = new ConfigurationOptions
        {
            EndPoints = { { "redis", 6379 }, },
            User = username,
            Password = password,
            SyncTimeout = 5000,
            ConnectTimeout = 5000
        };

        ConnectionMultiplexer redis = ConnectionMultiplexer.Connect(options);
        _pubsub = redis.GetDatabase(0);
    }

    public Boolean IsConnected()
    {
        if (_pubsub == null)
        {
            return false;
        }

        return _pubsub.Multiplexer.IsConnected;
    }

    public void Disconnect()
    {
        if (_pubsub != null)
        {
            _pubsub.Multiplexer.Close();
            _pubsub = null;
        }
    }

    public Boolean publish<T>(string channel, EventData<T> data)
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

    public class EventData<T>
    {
        public string @event { get; set; } = "";
        public T? data { get; set; }
    }
}
