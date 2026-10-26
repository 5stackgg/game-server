using System.Net;
using System.Net.Security;
using System.Net.Sockets;

namespace FiveStack.Simulator;

// The panel's local dev setup serves everything under .localhost, with
// mkcert's certificate. Browsers and curl send such names to the loopback
// (RFC 6761), which on a k3d node is where the ingress listens; a container's
// resolver has never heard of them and no container trusts mkcert.
public static class Network
{
    public static bool IsLocalhost(string host)
    {
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
    }

    public static EndPoint Target(DnsEndPoint endpoint)
    {
        return IsLocalhost(endpoint.Host)
            ? new IPEndPoint(IPAddress.Loopback, endpoint.Port)
            : endpoint;
    }

    public static SocketsHttpHandler Handler(Uri? target)
    {
        bool local = target != null && IsLocalhost(target.Host);

        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectCallback = Connect,
        };
        handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, errors) =>
            errors == SslPolicyErrors.None || local;

        return handler;
    }

    private static async ValueTask<Stream> Connect(
        SocketsHttpConnectionContext context,
        CancellationToken token
    )
    {
        DnsEndPoint endpoint = context.DnsEndPoint;
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

        try
        {
            await socket.ConnectAsync(Target(endpoint), token);

            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
