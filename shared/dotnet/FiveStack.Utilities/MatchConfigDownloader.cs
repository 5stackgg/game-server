using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging;

namespace FiveStack.Utilities;

// Never throws at its caller: the plugin starts this without awaiting it, so
// anything it threw would vanish as an unobserved task exception.
public class MatchConfigDownloader
{
    public static readonly string[] Configs =
    {
        "5stack.competitive.cfg",
        "5stack.duel.cfg",
        "5stack.knife.cfg",
        "5stack.lan.cfg",
        "5stack.rush.cfg",
        "5stack.warmup.cfg",
        "5stack.wingman.cfg",
    };

    public static readonly TimeSpan[] RetryDelays =
    {
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8),
    };

    private readonly ILogger _logger;
    private readonly HttpClient _http;
    private readonly IReadOnlyList<TimeSpan> _retryDelays;

    public MatchConfigDownloader(
        ILogger logger,
        HttpClient? http = null,
        IReadOnlyList<TimeSpan>? retryDelays = null
    )
    {
        _logger = logger;
        _http = http ?? HttpClientProvider.Client;
        _retryDelays = retryDelays ?? RetryDelays;
    }

    public async Task Download(string directoryPath)
    {
        foreach (var file in Configs)
        {
            string path = Path.Join(directoryPath, file);

            if (File.Exists(path))
            {
                continue;
            }

            _logger.LogInformation($"Downloading Config: {file}");

            string? content = await Fetch(file);

            if (content == null)
            {
                continue;
            }

            try
            {
                File.WriteAllText(path, content);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to write config {file}");
            }
        }
    }

    private async Task<string?> Fetch(string file)
    {
        string url =
            $"https://raw.githubusercontent.com/5stackgg/game-server/main/shared/cfg/{file}";

        for (int attempt = 0; ; attempt++)
        {
            bool canRetry = attempt < _retryDelays.Count;

            try
            {
                using HttpResponseMessage response = await _http.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }

                if (!canRetry || !IsTransient(response.StatusCode))
                {
                    _logger.LogError($"Failed to download config {file}: {response.StatusCode}");
                    return null;
                }

                _logger.LogWarning(
                    $"Failed to download config {file}: {response.StatusCode}, retrying"
                );
            }
            catch (Exception ex)
            {
                if (!canRetry)
                {
                    _logger.LogError(ex, $"Failed to download config {file}");
                    return null;
                }

                _logger.LogWarning(ex, $"Failed to download config {file}, retrying");
            }

            await Task.Delay(_retryDelays[attempt]);
        }
    }

    private static bool IsTransient(HttpStatusCode status)
    {
        return (int)status >= 500
            || status == HttpStatusCode.RequestTimeout
            || status == HttpStatusCode.TooManyRequests;
    }
}
