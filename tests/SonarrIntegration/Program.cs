// Runs the plugin's real SonarrClient against a real, throwaway Sonarr instance.
// Env: SONARR_URL, SONARR_API_KEY. Exit code 0 = Sonarr API still behaves as the plugin expects.
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Jellyfin.Plugin.WatchTracker.Configuration;
using Jellyfin.Plugin.WatchTracker.Services;

var url = Environment.GetEnvironmentVariable("SONARR_URL") ?? "http://localhost:8989";
var key = Environment.GetEnvironmentVariable("SONARR_API_KEY") ?? throw new Exception("SONARR_API_KEY not set");
const string TvdbId = "81189"; // Breaking Bad: Ended, 5 seasons + specials

var http = new HttpClient { BaseAddress = new Uri(url.TrimEnd('/') + "/") };
http.DefaultRequestHeaders.Add("X-Api-Key", key);
int fails = 0;
void Expect(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) fails++; }

// --- set up: root folder + series (no search, nothing downloaded)
var status = await http.GetFromJsonAsync<JsonObject>("api/v3/system/status");
Console.WriteLine("Sonarr " + status!["version"]);
var roots = await http.GetFromJsonAsync<JsonArray>("api/v3/rootfolder");
if (!roots!.Any(r => r!["path"]!.GetValue<string>() == "/tv"))
{
    (await http.PostAsJsonAsync("api/v3/rootfolder", new { path = "/tv" })).EnsureSuccessStatusCode();
}

var profiles = await http.GetFromJsonAsync<JsonArray>("api/v3/qualityprofile");
var lookup = await http.GetFromJsonAsync<JsonArray>($"api/v3/series/lookup?term=tvdb:{TvdbId}");
var series = (JsonObject)lookup![0]!.DeepClone();
series["qualityProfileId"] = profiles![0]!["id"]!.GetValue<int>();
series["rootFolderPath"] = "/tv";
series["monitored"] = true;
series["seasonFolder"] = true;
series["addOptions"] = new JsonObject { ["monitor"] = "all", ["searchForMissingEpisodes"] = false };
var add = await http.PostAsync("api/v3/series", new StringContent(series.ToJsonString(), System.Text.Encoding.UTF8, "application/json"));
Console.WriteLine("Add series: HTTP " + (int)add.StatusCode);
if (!add.IsSuccessStatusCode) Console.WriteLine(await add.Content.ReadAsStringAsync());
add.EnsureSuccessStatusCode();

async Task<JsonObject> Current() => (JsonObject)(await http.GetFromJsonAsync<JsonArray>($"api/v3/series?tvdbId={TvdbId}"))![0]!;
bool SeasonMonitored(JsonObject s, int n) => s["seasons"]!.AsArray().First(x => x!["seasonNumber"]!.GetValue<int>() == n)!["monitored"]!.GetValue<bool>();

var client = new SonarrClient(new Factory());
var cfg = new PluginConfiguration { SonarrUrl = url, SonarrApiKey = key, UnmonitorInSonarr = true };

Console.WriteLine(await client.TestAsync(url, key, default));

// --- season-level unmonitor
Console.WriteLine(await client.UnmonitorAsync(cfg, TvdbId, 1, default));
var s1 = await Current();
Expect(!SeasonMonitored(s1, 1), "Season 1 unmonitored");
Expect(SeasonMonitored(s1, 2), "Season 2 still monitored");
Expect(s1["monitored"]!.GetValue<bool>(), "Series still monitored after season unmonitor");

// --- whole-show unmonitor
Console.WriteLine(await client.UnmonitorAsync(cfg, TvdbId, null, default));
var s2 = await Current();
Expect(s2["seasons"]!.AsArray().All(x => !x!["monitored"]!.GetValue<bool>()), "All existing seasons unmonitored");
Expect(s2["monitored"]!.GetValue<bool>(), "Series still monitored (future seasons keep downloading)");

// --- unknown show / bad key
Expect((await client.UnmonitorAsync(cfg, "999999999", 1, default)) == "not in Sonarr", "Unknown TVDB id reported as not in Sonarr");
try { await client.TestAsync(url, "wrong-key", default); Expect(false, "Wrong API key rejected"); }
catch (Exception e) { Expect(e.Message.Contains("401"), "Wrong API key rejected"); }

Console.WriteLine(fails == 0 ? "ALL PASSED" : $"{fails} FAILED");
return fails == 0 ? 0 : 1;

class Factory : IHttpClientFactory { public HttpClient CreateClient(string name) => new(); }
