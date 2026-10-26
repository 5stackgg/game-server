using System.Collections.Concurrent;
using System.Net.Security;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace FiveStack.Simulator;

public interface IEventSink
{
    void Publish(Guid matchId, Guid mapId, string name, Dictionary<string, object> data);
}

public static class EventMessages
{
    // The plugin's EventData, wrapped the way NestJS's ws adapter routes it.
    public static string Envelope(
        Guid matchId,
        Guid mapId,
        Guid messageId,
        string name,
        Dictionary<string, object> data
    )
    {
        return JsonSerializer.Serialize(
            new
            {
                @event = "events",
                data = new
                {
                    @event = "",
                    matchId,
                    mapId,
                    messageId,
                    data = new { @event = name, data },
                },
            }
        );
    }

    // The api acknowledges by sending the message id back as a JSON string.
    public static Guid? Ack(string text)
    {
        return Guid.TryParse(text.Trim().Trim('"'), out Guid id) ? id : null;
    }
}

public static class Tls
{
    // A .localhost name is the panel's local dev setup, served with mkcert's
    // certificate, which no container trusts.
    public static bool Trusts(Uri uri, SslPolicyErrors errors)
    {
        return errors == SslPolicyErrors.None
            || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
    }

    public static RemoteCertificateValidationCallback For(Uri uri)
    {
        return (object _, X509Certificate? _, X509Chain? _, SslPolicyErrors errors) =>
            Trusts(uri, errors);
    }
}

// The plugin's MatchEvents: one ordered queue, every message kept until the api
// acknowledges it and resent after ten seconds without one.
public sealed class EventSocket : IEventSink
{
    private const int MaxPending = 1000;
    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(10);

    private readonly SimulatorSettings _settings;
    private readonly Func<Task> _onConnected;
    private readonly ConcurrentDictionary<Guid, (string Json, DateTime SentAt)> _pending = new();
    private readonly Channel<string> _outgoing = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true }
    );
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private ClientWebSocket? _socket;

    public EventSocket(SimulatorSettings settings, Func<Task> onConnected)
    {
        _settings = settings;
        _onConnected = onConnected;
    }

    public bool IsConnected => _socket?.State == WebSocketState.Open;

    public int PendingCount => _pending.Count;

    public void Publish(Guid matchId, Guid mapId, string name, Dictionary<string, object> data)
    {
        if (matchId == Guid.Empty || mapId == Guid.Empty)
        {
            return;
        }

        Guid messageId = Guid.NewGuid();
        string json = EventMessages.Envelope(matchId, mapId, messageId, name, data);

        _pending[messageId] = (json, DateTime.UtcNow);
        if (_pending.Count > MaxPending)
        {
            _pending.TryRemove(_pending.MinBy(entry => entry.Value.SentAt).Key, out _);
        }

        _outgoing.Writer.TryWrite(json);
    }

    public Task Run(CancellationToken token)
    {
        return Task.WhenAll(Connect(token), Drain(token), Retry(token));
    }

    private async Task Connect(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (_settings.ServerId == null || _settings.ApiPassword == null)
            {
                Log.Warn(
                    "cannot connect to the api socket: missing SERVER_ID / SERVER_API_PASSWORD"
                );
                await Task.Delay(TimeSpan.FromSeconds(10), token);
                continue;
            }

            var uri = new Uri($"{_settings.WsUrl}/matches");
            var socket = new ClientWebSocket();

            try
            {
                socket.Options.SetRequestHeader(
                    "Authorization",
                    $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ServerId}:{_settings.ApiPassword}"))}"
                );
                socket.Options.RemoteCertificateValidationCallback = Tls.For(uri);

                await socket.ConnectAsync(uri, token);
                _socket = socket;
                Log.Info($"connected to {uri}");

                _ = _onConnected();

                await Receive(socket, token);
            }
            catch (Exception error) when (!token.IsCancellationRequested)
            {
                Log.Warn($"api socket {uri}: {error.Message}");
            }
            finally
            {
                _socket = null;
                socket.Dispose();
            }

            await Task.Delay(TimeSpan.FromSeconds(3), token);
        }
    }

    private async Task Receive(ClientWebSocket socket, CancellationToken token)
    {
        var buffer = new byte[256];

        while (socket.State == WebSocketState.Open)
        {
            WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, token);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                Log.Info("api socket closed");
                return;
            }

            if (EventMessages.Ack(Encoding.UTF8.GetString(buffer, 0, result.Count)) is Guid acked)
            {
                _pending.TryRemove(acked, out _);
            }
        }
    }

    private async Task Drain(CancellationToken token)
    {
        await foreach (string json in _outgoing.Reader.ReadAllAsync(token))
        {
            await Send(json, token);
        }
    }

    private async Task Retry(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), token);

            DateTime now = DateTime.UtcNow;

            foreach (var (messageId, message) in _pending.ToArray())
            {
                if (now - message.SentAt < RetryAfter || !IsConnected)
                {
                    continue;
                }

                await Send(message.Json, token);
                _pending.TryUpdate(messageId, (message.Json, now), message);
            }
        }
    }

    private async Task Send(string json, CancellationToken token)
    {
        ClientWebSocket? socket = _socket;
        if (socket?.State != WebSocketState.Open)
        {
            return;
        }

        await _sendLock.WaitAsync(token);
        try
        {
            await socket.SendAsync(
                Encoding.UTF8.GetBytes(json),
                WebSocketMessageType.Text,
                true,
                token
            );
        }
        catch (Exception error) when (!token.IsCancellationRequested)
        {
            Log.Warn($"unable to send event: {error.Message}");
        }
        finally
        {
            _sendLock.Release();
        }
    }
}
