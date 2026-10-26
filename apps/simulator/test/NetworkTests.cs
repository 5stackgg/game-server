using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using FiveStack.Simulator;
using Xunit;

public class NetworkTests
{
    [Fact]
    public async Task ALocalhostNameReachesTheLoopbackWithoutDns()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        Task answer = Task.Run(async () =>
        {
            using TcpClient client = await listener.AcceptTcpClientAsync();
            NetworkStream stream = client.GetStream();
            var buffer = new byte[4096];
            await stream.ReadAsync(buffer);
            await stream.WriteAsync(
                Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"
                )
            );
        });

        using var http = new HttpClient(
            Network.Handler(new Uri($"http://api.5stack.localhost:{port}"))
        );
        string body = await http.GetStringAsync($"http://api.5stack.localhost:{port}/");

        await answer;
        listener.Stop();
        Assert.Equal("ok", body);
    }

    private static X509Certificate2 SelfSigned()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=*.5stack.localhost",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        using X509Certificate2 certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1)
        );

        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), null);
    }

    private static (int Port, Task Served) ServeTls(X509Certificate2 certificate)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        Task served = Task.Run(async () =>
        {
            try
            {
                using TcpClient client = await listener.AcceptTcpClientAsync();
                using var tls = new SslStream(client.GetStream());
                await tls.AuthenticateAsServerAsync(certificate);
                var buffer = new byte[4096];
                await tls.ReadAsync(buffer);
                await tls.WriteAsync(
                    Encoding.ASCII.GetBytes(
                        "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"
                    )
                );
            }
            catch (Exception) { }
            finally
            {
                listener.Stop();
            }
        });

        return (((IPEndPoint)listener.LocalEndpoint).Port, served);
    }

    [Fact]
    public async Task TheLocalDevSetupsCertificateIsTakenOnTrust()
    {
        using X509Certificate2 certificate = SelfSigned();
        (int port, Task served) = ServeTls(certificate);
        var uri = new Uri($"https://api.5stack.localhost:{port}/");

        using var http = new HttpClient(Network.Handler(uri));

        Assert.Equal("ok", await http.GetStringAsync(uri));
        await served;
    }

    [Fact]
    public async Task AnyOtherHostStillNeedsATrustedCertificate()
    {
        using X509Certificate2 certificate = SelfSigned();
        (int port, Task served) = ServeTls(certificate);
        var uri = new Uri($"https://127.0.0.1:{port}/");

        using var http = new HttpClient(Network.Handler(uri));

        await Assert.ThrowsAsync<HttpRequestException>(() => http.GetStringAsync(uri));
        await served;
    }

    [Fact]
    public void ALocalhostNameIsDialledAtTheLoopback()
    {
        Assert.Equal(
            new IPEndPoint(IPAddress.Loopback, 443),
            Network.Target(new DnsEndPoint("api.5stack.localhost", 443))
        );
        Assert.Equal(
            new DnsEndPoint("api.5stack.gg", 443),
            Network.Target(new DnsEndPoint("api.5stack.gg", 443))
        );
    }

    [Theory]
    [InlineData("localhost", true)]
    [InlineData("api.5stack.localhost", true)]
    [InlineData("API.5STACK.LOCALHOST", true)]
    [InlineData("localhost.5stack.gg", false)]
    [InlineData("api.5stack.gg", false)]
    public void OnlyLocalhostNamesAreTheLoopback(string host, bool loopback)
    {
        Assert.Equal(loopback, Network.IsLocalhost(host));
    }
}
