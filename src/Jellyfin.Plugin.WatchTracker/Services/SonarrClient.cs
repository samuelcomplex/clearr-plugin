using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.WatchTracker.Configuration;

namespace Jellyfin.Plugin.WatchTracker.Services;

/// <summary>Minimal Sonarr v3 API client: unmonitor a season or a whole series.</summary>
public class SonarrClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    public SonarrClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public static bool IsConfigured(PluginConfiguration c) =>
        c.UnmonitorInSonarr && !string.IsNullOrWhiteSpace(c.SonarrUrl) && !string.IsNullOrWhiteSpace(c.SonarrApiKey);

    /// <summary>
    /// Unmonitors one season (seasonNumber != null) or all existing seasons in Sonarr. The series stays monitored.
    /// Returns a human-readable note. Throws on HTTP/API errors so the caller can skip deleting.
    /// </summary>
    public async Task<string> UnmonitorAsync(PluginConfiguration c, string? tvdbId, int? seasonNumber, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tvdbId))
        {
            return "no TVDB id, Sonarr not touched";
        }

        using var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        var baseUrl = c.SonarrUrl.TrimEnd('/');

        using var getReq = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/v3/series?tvdbId={Uri.EscapeDataString(tvdbId)}");
        getReq.Headers.Add("X-Api-Key", c.SonarrApiKey);
        using var getResp = await client.SendAsync(getReq, ct).ConfigureAwait(false);
        getResp.EnsureSuccessStatusCode();
        var arr = JsonNode.Parse(await getResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false)) as JsonArray;
        var series = arr?.FirstOrDefault() as JsonObject;
        if (series is null)
        {
            return "not in Sonarr";
        }

        var id = series["id"]?.GetValue<int>() ?? throw new InvalidOperationException("Sonarr series has no id");
        var seasons = series["seasons"] as JsonArray;

        // The series itself always stays monitored, so Sonarr still picks up future seasons
        // (subject to the series' own "Monitor New Seasons" setting). Only existing seasons are unmonitored.
        if (seasons != null)
        {
            foreach (var s in seasons.OfType<JsonObject>())
            {
                if (seasonNumber is null || s["seasonNumber"]?.GetValue<int>() == seasonNumber)
                {
                    s["monitored"] = false;
                }
            }
        }

        using var putReq = new HttpRequestMessage(HttpMethod.Put, $"{baseUrl}/api/v3/series/{id}")
        {
            Content = new StringContent(series.ToJsonString(), Encoding.UTF8, "application/json")
        };
        putReq.Headers.Add("X-Api-Key", c.SonarrApiKey);
        using var putResp = await client.SendAsync(putReq, ct).ConfigureAwait(false);
        putResp.EnsureSuccessStatusCode();

        return seasonNumber is null ? "existing seasons unmonitored in Sonarr (show still monitored for new seasons)" : $"season {seasonNumber} unmonitored in Sonarr";
    }

    public async Task<string> TestAsync(PluginConfiguration c, CancellationToken ct)
    {
        using var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);
        using var req = new HttpRequestMessage(HttpMethod.Get, c.SonarrUrl.TrimEnd('/') + "/api/v3/system/status");
        req.Headers.Add("X-Api-Key", c.SonarrApiKey);
        using var resp = await client.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var node = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        return "Connected to Sonarr " + (node?["version"]?.GetValue<string>() ?? "?");
    }
}
