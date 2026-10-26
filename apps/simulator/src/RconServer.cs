using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace FiveStack.Simulator;

public static class RconPacket
{
    public const int ResponseValue = 0;
    public const int ExecCommand = 2;
    public const int AuthResponse = 2;
    public const int Auth = 3;

    public static byte[] Encode(int id, int type, string body)
    {
        byte[] payload = Encoding.UTF8.GetBytes(body);
        var packet = new byte[payload.Length + 14];

        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(0), payload.Length + 10);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(4), id);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(8), type);
        payload.CopyTo(packet, 12);

        return packet;
    }

    public static async Task<(int Id, int Type, string Body)?> Read(
        Stream stream,
        CancellationToken token
    )
    {
        var header = new byte[4];
        if (!await Fill(stream, header, token))
        {
            return null;
        }

        int size = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (size < 10 || size > 1 << 20)
        {
            return null;
        }

        var rest = new byte[size];
        if (!await Fill(stream, rest, token))
        {
            return null;
        }

        int id = BinaryPrimitives.ReadInt32LittleEndian(rest.AsSpan(0));
        int type = BinaryPrimitives.ReadInt32LittleEndian(rest.AsSpan(4));
        string body = Encoding.UTF8.GetString(rest, 8, size - 10);

        return (id, type, body);
    }

    private static async Task<bool> Fill(Stream stream, byte[] buffer, CancellationToken token)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int count = await stream.ReadAsync(buffer.AsMemory(read), token);
            if (count == 0)
            {
                return false;
            }
            read += count;
        }

        return true;
    }
}

// Source RCON over TCP on the game port, which is where the api's rcon-client
// looks for it.
public sealed class RconServer
{
    private readonly int _port;
    private readonly string _password;
    private readonly Func<string, string> _execute;

    public RconServer(int port, string password, Func<string, string> execute)
    {
        _port = port;
        _password = password;
        _execute = execute;
    }

    public int Port { get; private set; }

    public async Task Run(CancellationToken token, TaskCompletionSource? listening = null)
    {
        var listener = new TcpListener(IPAddress.Any, _port);
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listening?.TrySetResult();
        Log.Info($"rcon listening on {Port}");

        try
        {
            while (!token.IsCancellationRequested)
            {
                TcpClient client = await listener.AcceptTcpClientAsync(token);
                _ = Handle(client, token);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            listener.Stop();
        }
    }

    private async Task Handle(TcpClient client, CancellationToken token)
    {
        using (client)
        {
            NetworkStream stream = client.GetStream();
            bool authenticated = false;

            try
            {
                while (await RconPacket.Read(stream, token) is var (id, type, body))
                {
                    if (type == RconPacket.Auth)
                    {
                        authenticated = _password.Length > 0 && body == _password;

                        if (authenticated)
                        {
                            await stream.WriteAsync(
                                RconPacket.Encode(id, RconPacket.ResponseValue, ""),
                                token
                            );
                        }

                        await stream.WriteAsync(
                            RconPacket.Encode(authenticated ? id : -1, RconPacket.AuthResponse, ""),
                            token
                        );
                        continue;
                    }

                    if (!authenticated)
                    {
                        return;
                    }

                    string response = type == RconPacket.ExecCommand ? _execute(body) : "";
                    await stream.WriteAsync(
                        RconPacket.Encode(id, RconPacket.ResponseValue, response),
                        token
                    );
                }
            }
            catch (Exception error) when (error is IOException or OperationCanceledException)
            {
                // the api drops its connection after a few idle seconds
            }
        }
    }
}
