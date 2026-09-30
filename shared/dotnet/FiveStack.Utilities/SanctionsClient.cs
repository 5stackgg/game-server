using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FiveStack.Entities.PlayerManagement;

namespace FiveStack.Utilities;

public sealed record SanctionSync(List<PlayerSanction>? Sanctions, string? Error);

// Never throws at its caller: a panel that is down must not take a public
// server's chat or voice down with it, so every failure comes back as Error.
public class SanctionsClient
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;

    public SanctionsClient(HttpClient? http = null)
    {
        _http = http ?? HttpClientProvider.Client;
    }

    public async Task<SanctionSync> Sync(
        PlayerManagementSettings settings,
        PlayerSanctionsRequest body
    )
    {
        if (!settings.IsConnected())
        {
            return new SanctionSync(null, "not configured");
        }

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Post, settings.SyncUrl());
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                settings.SERVER_API_PASSWORD
            );
            request.Content = new StringContent(
                JsonSerializer.Serialize(body, Json),
                Encoding.UTF8,
                "application/json"
            );

            using CancellationTokenSource timeout = new(RequestTimeout);
            using HttpResponseMessage response = await _http.SendAsync(request, timeout.Token);
            string text = await response.Content.ReadAsStringAsync(timeout.Token);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return new SanctionSync(
                    null,
                    "401 unauthorized; check SERVER_ID and SERVER_API_PASSWORD"
                );
            }

            if (!response.IsSuccessStatusCode)
            {
                return new SanctionSync(
                    null,
                    $"{(int)response.StatusCode} {(text.Length > 200 ? text[..200] : text)}".Trim()
                );
            }

            PlayerSanctionsResponse? parsed = JsonSerializer.Deserialize<PlayerSanctionsResponse>(
                text,
                Json
            );

            return new SanctionSync(parsed?.sanctions ?? new List<PlayerSanction>(), null);
        }
        catch (Exception error)
        {
            return new SanctionSync(null, error.Message);
        }
    }
}
