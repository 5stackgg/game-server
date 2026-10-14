using System.Net;
using System.Net.Http;
using System.Text;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using Xunit;

// The plugin starts the download without awaiting it, so anything Download
// throws is an unobserved task exception nobody ever sees.
public class MatchConfigDownloaderTests : IDisposable
{
    private const string Competitive = "5stack.competitive.cfg";

    private static readonly TimeSpan[] NoBackoff = { TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero };

    private readonly string _directory = Directory.CreateTempSubdirectory("5stack-cfg-").FullName;

    public void Dispose()
    {
        Directory.Delete(_directory, true);
    }

    private sealed class Logged : ILogger
    {
        public readonly List<(LogLevel Level, Exception? Exception, string Message)> Entries =
            new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            Entries.Add((logLevel, exception, formatter(state, exception)));
        }
    }

    private sealed class GitHub : HttpMessageHandler
    {
        private readonly Func<string, int, HttpResponseMessage> _respond;

        public readonly Dictionary<string, int> Requests = new();

        public GitHub(Func<string, int, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string file = Path.GetFileName(request.RequestUri!.AbsolutePath);
            int attempt = Requests.GetValueOrDefault(file);
            Requests[file] = attempt + 1;

            return Task.FromResult(_respond(file, attempt));
        }
    }

    private static HttpResponseMessage Cfg(string file)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($"// {file}", Encoding.UTF8),
        };
    }

    private Task Download(GitHub github, Logged logged)
    {
        return new MatchConfigDownloader(logged, new HttpClient(github), NoBackoff).Download(
            _directory
        );
    }

    private string OnDisk(string file)
    {
        return Path.Join(_directory, file);
    }

    [Fact]
    public async Task ANetworkFailureIsLoggedWithItsExceptionInsteadOfThrown()
    {
        GitHub github = new(
            (file, _) =>
                file == Competitive
                    ? throw new HttpRequestException("Name or service not known")
                    : Cfg(file)
        );
        Logged logged = new();

        await Download(github, logged);

        Assert.False(File.Exists(OnDisk(Competitive)));
        Assert.Contains(
            logged.Entries,
            entry =>
                entry.Level == LogLevel.Error
                && entry.Exception is HttpRequestException
                && entry.Message.Contains(Competitive)
        );
        Assert.All(
            MatchConfigDownloader.Configs.Where(file => file != Competitive),
            file => Assert.True(File.Exists(OnDisk(file)))
        );
    }

    [Fact]
    public async Task ItGivesUpOnAConfigAfterItsRetries()
    {
        GitHub github = new((_, _) => throw new HttpRequestException("Connection refused"));

        await Download(github, new Logged());

        Assert.Equal(NoBackoff.Length + 1, github.Requests[Competitive]);
    }

    [Fact]
    public async Task ATransientFailureIsRetried()
    {
        GitHub github = new(
            (file, attempt) =>
                (file, attempt) switch
                {
                    (Competitive, 0) => throw new HttpRequestException("Connection reset"),
                    (Competitive, 1) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
                    _ => Cfg(file),
                }
        );

        await Download(github, new Logged());

        Assert.Equal(3, github.Requests[Competitive]);
        Assert.Equal($"// {Competitive}", File.ReadAllText(OnDisk(Competitive)));
    }

    [Fact]
    public async Task AConfigThatIsNotThereIsNotRetried()
    {
        GitHub github = new((_, _) => new HttpResponseMessage(HttpStatusCode.NotFound));
        Logged logged = new();

        await Download(github, logged);

        Assert.All(MatchConfigDownloader.Configs, file => Assert.Equal(1, github.Requests[file]));
        Assert.Contains(
            logged.Entries,
            entry => entry.Level == LogLevel.Error && entry.Message.Contains(Competitive)
        );
    }

    [Fact]
    public async Task AWriteFailureIsLoggedWithItsExceptionInsteadOfThrown()
    {
        Directory.CreateDirectory(OnDisk(Competitive));
        GitHub github = new((file, _) => Cfg(file));
        Logged logged = new();

        await Download(github, logged);

        Assert.Contains(
            logged.Entries,
            entry =>
                entry.Level == LogLevel.Error
                && entry.Exception != null
                && entry.Message.Contains(Competitive)
        );
        Assert.True(File.Exists(OnDisk("5stack.wingman.cfg")));
    }

    [Fact]
    public async Task AConfigAlreadyOnDiskIsKept()
    {
        File.WriteAllText(OnDisk(Competitive), "mp_freezetime 3");
        GitHub github = new((file, _) => Cfg(file));

        await Download(github, new Logged());

        Assert.False(github.Requests.ContainsKey(Competitive));
        Assert.Equal("mp_freezetime 3", File.ReadAllText(OnDisk(Competitive)));
    }
}
