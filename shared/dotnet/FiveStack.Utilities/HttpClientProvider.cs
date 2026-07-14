using System.Net.Http;

namespace FiveStack.Utilities
{
    public static class HttpClientProvider
    {
        public static readonly HttpClient Client = new HttpClient(
            new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }
        );
    }
}
