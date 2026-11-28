// Plugin tests (offline, against the Jellyfin 10.11 stubs): run with
//   dotnet run --project tests/PluginTests -p:UseStubs=true
using System.Text.Json.Nodes;
using Clearr.Core;
using Clearr.Tests;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Clearr.Api;
using Jellyfin.Plugin.Clearr.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Jellyfin.Data.Enums;

var fails = 0;
void Expect(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) fails++; }
var root = Path.Combine(Path.GetTempPath(), "clearr-plugin-" + Guid.NewGuid().ToString("N")[..6]);
var now = DateTime.UtcNow;

// ---------------------------------------------------------------- fake Jellyfin internals
var lib = new FakeLib(); var users = new FakeUsers(); var data = new FakeData();
User A = users.Add("Anna"), B = users.Add("Bjorn");

var (managed, mSeasons, mEps) = lib.AddSeries(root, "Managed Show", SeriesStatus.Ended, 81189, new[] { 2, 2 }, seasonFolders: true);
foreach (var e in mEps.Where(e => e.ParentIndexNumber == 1)) { data.Set(A, e, true, now.AddDays(-30)); data.Set(B, e, true, now.AddDays(-25)); }
var (flat, fSeasons, fEps) = lib.AddSeries(root, "Flat Show", SeriesStatus.Ended, 999001, new[] { 2, 1 }, seasonFolders: false);
foreach (var e in fEps.Where(e => e.ParentIndexNumber == 1)) data.Set(A, e, true, now.AddDays(-30));
var (whole, _, wEps) = lib.AddSeries(root, "Whole Show", SeriesStatus.Ended, 999002, new[] { 1 }, seasonFolders: true);
foreach (var e in wEps) data.Set(A, e, true, now.AddDays(-30));
var movie = lib.AddMovie(root, "Some Movie", 2020, 603);
data.Set(A, movie, true, now.AddDays(-20));
data.Set(B, mEps.First(e => e.ParentIndexNumber == 2), false, now.AddDays(-2), ticks: 5_000_000);

data.Favourite(B, whole);
data.Favourite(A, mSeasons[1]);
var favMovie = lib.AddMovie(root, "Favourite Movie", 2021, 604);
data.Favourite(A, favMovie);

var source = new JellyfinInternalSource(lib, users, data);
var snap = await source.LoadAsync(default);
var ms = snap.Shows.Single(s => s.Name == "Managed Show");
Expect(snap.Users.Count == 2 && snap.Users[0].Id == A.Id.ToString("N"), "Users read, ids in Jellyfin API form (32 hex)");
Expect(ms.Status == "Ended" && ms.ProviderIds["Tvdb"] == "81189" && ms.Seasons.Count == 2 && ms.Seasons.All(x => x.Episodes.Count == 2), "Show, status, provider ids, seasons and episodes read");
Expect(snap.Movies.Single(m => m.Name == "Some Movie").Year == 2020 && snap.Movies.Single(m => m.Name == "Some Movie").ProviderIds["Tmdb"] == "603", "Movies read with year and TMDB id");
Expect(snap.Get(mEps[0].Id.ToString("N"), A.Id.ToString("N"))?.Played == true, "Played state read");
var partial = snap.Get(mEps.First(e => e.ParentIndexNumber == 2).Id.ToString("N"), B.Id.ToString("N"));
Expect(partial is { Played: false, PositionTicks: 5_000_000 }, "Resume position read");
Expect(snap.Get(mEps[3].Id.ToString("N"), A.Id.ToString("N")) == null, "Untouched items not listed");
Expect(snap.Favorites[whole.Id.ToString("N")].SetEquals(new[] { B.Id.ToString("N") }) && snap.Favorites.ContainsKey(mSeasons[1].Id.ToString("N")) && snap.Favorites.ContainsKey(favMovie.Id.ToString("N")) && !snap.Favorites.ContainsKey(movie.Id.ToString("N")) && !snap.Favorites.ContainsKey(managed.Id.ToString("N")), "Favourite shows, seasons and movies read per user");
source.RefreshAsync(default).Wait();
Expect(lib.ScansQueued == 1, "Refresh queues a Jellyfin library scan");

// ---------------------------------------------------------------- fallback deleter on real folders
var del = new JellyfinDirectDeleter(lib);
var mt = new DirectTarget("Movie", movie.Id.ToString("N"), null, null);
Expect(del.Measure(mt) > 0, "Measures a movie's size on disk");
var wt = new DirectTarget("Show", whole.Id.ToString("N"), null, null);
var wsize = del.Measure(wt);
Expect(wsize > 0 && del.Delete(wt).Contains("Jellyfin") && !Directory.Exists(whole.Path), "Whole show: folder deleted");
var ft = new DirectTarget("Season", flat.Id.ToString("N"), fSeasons[0].Id.ToString("N"), 1);
del.Delete(ft);
Expect(fEps.Where(e => e.ParentIndexNumber == 1).All(e => !File.Exists(e.Path)) && fEps.Where(e => e.ParentIndexNumber == 2).All(e => File.Exists(e.Path)) && Directory.Exists(flat.Path), "Flat layout: only that season's episode files deleted");
var nested = lib.AddSeries(root + "/Managed Show", "Nested", SeriesStatus.Ended, 5, new[] { 1 }, seasonFolders: true).Item1;
try { del.Delete(new DirectTarget("Show", managed.Id.ToString("N"), null, null)); Expect(false, "Refuses to delete a folder containing another show"); }
catch (InvalidOperationException ex) { Expect(ex.Message.Contains("Nested") && Directory.Exists(managed.Path), "Refuses to delete a folder containing another show"); }
lib.Items.Remove(nested);

// ---------------------------------------------------------------- migration from Watch Tracker + host + API with fake Sonarr
var w = new FakeWorld();
w.SonarrSeries[7] = new JsonObject { ["id"] = 7, ["title"] = "Managed Show", ["tvdbId"] = 81189, ["monitored"] = true, ["tags"] = new JsonArray(),
    ["seasons"] = new JsonArray(new JsonObject { ["seasonNumber"] = 1, ["monitored"] = true }, new JsonObject { ["seasonNumber"] = 2, ["monitored"] = true }) };
w.EpisodeFiles.Add((1, 7, 1, 3L << 30)); w.EpisodeFiles.Add((2, 7, 1, 3L << 30)); w.EpisodeFiles.Add((3, 7, 2, 3L << 30)); w.EpisodeFiles.Add((4, 7, 2, 3L << 30));
var (fake, url) = await FakeServers.StartAsync(w);

var dataPath = Path.Combine(root, "_data");
Directory.CreateDirectory(Path.Combine(dataPath, "watchtracker"));
File.WriteAllText(Path.Combine(dataPath, "watchtracker", "keep.json"), $"[\"{flat.Id}\"]");
File.WriteAllText(Path.Combine(dataPath, "watchtracker", "history.json"), "[{\"date\":\"2026-11-25T22:15:03Z\",\"action\":\"Deleted\",\"kind\":\"Series\",\"seriesName\":\"Old Show\",\"seasonName\":null,\"detail\":\"x\",\"bytes\":5}]");
_ = new TestPlugin(new Jellyfin.Plugin.Clearr.Configuration.PluginConfiguration
{
    Mode = "DryRun", GraceDays = 3, SonarrUrl = url + "/sonarr", SonarrApiKey = "sonarr-key", ExcludedUserIds = new[] { B.Id.ToString() }, DeleteSeries = false,
});
var host = new ClearrHost(new FakePaths(dataPath), lib, users, data, new Factory(), NullLoggerFactory.Instance);
var st = host.Settings.Get();
Expect(st.ShowsMode == CleanupMode.DryRun && st.MoviesMode == CleanupMode.DryRun && st.GraceDays == 3 && !st.DeleteShows && st.Sonarr.Url.EndsWith("/sonarr") && !st.AllowDirectDelete, "Migrated Watch Tracker settings (Jellyfin fallback left off)");
Expect(st.ExcludedUserIds.SequenceEqual(new[] { B.Id.ToString("N") }), "Migrated ignored users to Clearr's id form");
Expect(host.Keep.Contains(flat.Id.ToString("N")), "Migrated Watch Tracker keep list");
Expect(host.History.GetAll().Any(h => h.Title == "Old Show" && h.Kind == "Show"), "Migrated Watch Tracker history");
st.ExcludedUserIds.Clear(); host.Settings.Save(st);

var api = new ClearrController(host, new Factory());
JsonNode Body(ActionResult r) => JsonNode.Parse(((ContentResult)r).Content!)!;
var rep = Body(await api.Report(default));
var shows = rep["shows"]!.AsArray();
var mShow = shows.First(s => s!["name"]!.GetValue<string>() == "Managed Show")!;
Expect(mShow["via"]!.GetValue<string>() == "Sonarr" && mShow["seasons"]![0]!["eligible"]!.GetValue<bool>(), "API report (camelCase, same shape as Clearr): managed S1 eligible via Sonarr");
Expect(shows.First(s => s!["name"]!.GetValue<string>() == "Flat Show")!["kept"]!.GetValue<bool>(), "Migrated keep applies in the report");
Expect(rep["movies"]![0]!["via"] == null && !rep["movies"]![0]!["eligible"]!.GetValue<bool>(), "Unmanaged movie not removable while the fallback is off");

var guard = (ContentResult)await api.Cleanup(false, null, default);
Expect(guard.StatusCode == 409 && w.Log.Count == 0, "API refuses real cleanup in Dry run");
var put = new JsonObject { ["settings"] = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(host.Settings.Get(), Clearr.Storage.Json.Options)) };
put["settings"]!["allowDirectDelete"] = true;
put["settings"]!["showsMode"] = "Delete";
put["settings"]!["moviesMode"] = "Delete";
put["settings"]!["https"] = new JsonObject { ["enabled"] = true, ["port"] = 1 };
Expect(((ContentResult)api.PutSettings(put)).StatusCode == 200 && host.Settings.Get().AllowDirectDelete && !host.Settings.Get().Https.Enabled, "Settings saved; app-only parts (HTTPS) ignored");
var status = Body(api.Status());
Expect(status["showsMode"]!.GetValue<string>() == "Delete" && status["moviesMode"]!.GetValue<string>() == "Delete" && status["language"] != null, "Status reports both modes and the language");
var dry = Body(await api.Cleanup(true, null, default));
var titles = dry["actions"]!.AsArray().Select(a => $"{a!["action"]} {a["title"]} {a["seasonName"]}").ToList();
Console.WriteLine("  dry: " + string.Join(" | ", titles));
Expect(titles.Contains("WouldDelete Managed Show Season 1") && titles.Any(t => t.StartsWith("WouldDelete Some Movie")), "Dry run: Sonarr season + Jellyfin-fallback movie");
var reason = dry["actions"]!.AsArray().First(a => a!["title"]!.GetValue<string>() == "Managed Show")!["parts"]![0]!;
Expect(reason["code"]!.GetValue<string>() == "reason.ready", "Results carry translatable reasons");
var tasks = typeof(ShowsCleanupTask).Assembly.GetTypes().Where(t => typeof(MediaBrowser.Model.Tasks.IScheduledTask).IsAssignableFrom(t) && !t.IsAbstract).Select(t => (MediaBrowser.Model.Tasks.IScheduledTask)Activator.CreateInstance(t, host)!).ToList();
Expect(tasks.Select(t => t.Key).OrderBy(k => k).SequenceEqual(new[] { "ClearrMovies", "ClearrShows" }) && tasks.All(t => t.Category == "Clearr"), "Two scheduled tasks: Clear finished shows, Clear finished movies");
var real = Body(await api.Cleanup(false, null, default));
Expect(!w.EpisodeFiles.Any(f => f.SeriesId == 7 && f.Season == 1) && w.EpisodeFiles.Count(f => f.SeriesId == 7 && f.Season == 2) == 2, "Real run: Sonarr deleted season 1 files only");
Expect(!File.Exists(movie.Path) && lib.ScansQueued >= 2, "Real run: unmanaged movie deleted through Jellyfin, library scan queued");
var ui = api.Ui("clearr-ui.js") as FileStreamResult;
Expect(ui != null && ui.ContentType.StartsWith("text/javascript"), "Shared UI script served by the plugin");
Expect(api.Ui("lang-sv.json") is FileStreamResult { ContentType: var lt } && lt.StartsWith("application/json"), "Language files served by the plugin");

var summary = JsonNode.Parse(((ContentResult)api.Summary()).Content!)!;
Expect(summary["removedTotal"]!.GetValue<int>() >= 2 && summary["freedTotalGb"] is not null && summary["showsMode"] is not null, "Summary endpoint for Home Assistant");
Expect(((ContentResult)await api.TestNotifications(new JsonObject(), default)).StatusCode == 400, "Notification test without a target is refused");

var backup = Body(api.GetBackup());
var jfBefore = host.Settings.Get().Jellyfin.Url;
backup["settings"]!["graceDays"] = 11;
backup["settings"]!["jellyfin"]!["url"] = "http://elsewhere:8096";
backup["keep"] = new JsonArray("restored-id");
var restored = api.RestoreBackup(backup);
Expect(((ContentResult)restored).StatusCode == 200 && host.Settings.Get().GraceDays == 11 && host.Settings.Get().Jellyfin.Url == jfBefore && host.Keep.Contains("restored-id"), "Backup round trip through the plugin API; the Jellyfin connection is left alone");
Expect(((ContentResult)api.RestoreBackup(JsonNode.Parse("{}"))).StatusCode == 400, "A file that isn't a backup is refused");

await fake.StopAsync();
Console.WriteLine(fails == 0 ? "\nALL PASSED" : $"\n{fails} FAILED");
try { Directory.Delete(root, true); } catch { }
return fails == 0 ? 0 : 1;

// ---------------------------------------------------------------- fakes
class TestPlugin : Jellyfin.Plugin.Clearr.Plugin { public TestPlugin(Jellyfin.Plugin.Clearr.Configuration.PluginConfiguration c) : base(null!, null!) { Configuration = c; } }
class Factory : IHttpClientFactory { public HttpClient CreateClient(string n) => new(new HttpClientHandler { UseProxy = false }); }
class FakePaths : IApplicationPaths { public FakePaths(string d) { DataPath = d; } public string DataPath { get; } public string PluginConfigurationsPath => DataPath; }
class FakeUsers : IUserManager
{
    public List<User> All = new();
    public User Add(string n) { var u = new User { Id = Guid.NewGuid(), Username = n }; All.Add(u); return u; }
    public IEnumerable<User> GetUsers() => All;
    public User? GetUserById(Guid id) => All.FirstOrDefault(u => u.Id == id);
}
class FakeData : IUserDataManager
{
    readonly Dictionary<(Guid, Guid), UserItemData> d = new();
    public void Set(User u, BaseItem i, bool played, DateTime at, long ticks = 0) => d[(u.Id, i.Id)] = new UserItemData { Played = played, LastPlayedDate = at, PlaybackPositionTicks = ticks };
    public void Favourite(User u, BaseItem i) { if (!d.TryGetValue((u.Id, i.Id), out var x)) d[(u.Id, i.Id)] = x = new UserItemData(); x.IsFavorite = true; }
    public UserItemData? GetUserData(User user, BaseItem item) => d.TryGetValue((user.Id, item.Id), out var x) ? x : new UserItemData();
}
class FakeLib : ILibraryManager
{
    public List<BaseItem> Items = new();
    public int ScansQueued;
    public (Series, List<Season>, List<Episode>) AddSeries(string root, string name, SeriesStatus st, int tvdb, int[] seasons, bool seasonFolders)
    {
        var dir = Path.Combine(root, name); Directory.CreateDirectory(dir);
        var s = new Series { Id = Guid.NewGuid(), Name = name, Path = dir, Status = st, ProviderIds = new() { ["Tvdb"] = tvdb.ToString() } };
        Items.Add(s); var ss = new List<Season>(); var es = new List<Episode>();
        for (int n = 1; n <= seasons.Length; n++)
        {
            var sdir = seasonFolders ? Path.Combine(dir, $"Season {n}") : null;
            if (sdir != null) Directory.CreateDirectory(sdir);
            var se = new Season { Id = Guid.NewGuid(), Name = $"Season {n}", SeriesId = s.Id, IndexNumber = n, Path = sdir ?? "" };
            Items.Add(se); ss.Add(se);
            for (int e = 1; e <= seasons[n - 1]; e++)
            {
                var f = Path.Combine(sdir ?? dir, $"{name} S{n:00}E{e:00}.mkv"); File.WriteAllText(f, new string('x', 1000));
                var ep = new Episode { Id = Guid.NewGuid(), Name = $"Ep {e}", SeriesId = s.Id, SeasonId = se.Id, ParentIndexNumber = n, IndexNumber = e, Path = f };
                Items.Add(ep); es.Add(ep);
            }
        }
        return (s, ss, es);
    }
    public Movie AddMovie(string root, string name, int year, int tmdb)
    {
        var dir = Path.Combine(root, "movies", name); Directory.CreateDirectory(dir);
        var f = Path.Combine(dir, name + ".mkv"); File.WriteAllText(f, new string('x', 5000));
        var m = new Movie { Id = Guid.NewGuid(), Name = name, ProductionYear = year, Path = f, ProviderIds = new() { ["Tmdb"] = tmdb.ToString() } };
        Items.Add(m); return m;
    }
    public BaseItem? GetItemById(Guid id) => Items.FirstOrDefault(i => i.Id == id);
    public IReadOnlyList<BaseItem> GetItemList(InternalItemsQuery q) => Items.Where(i =>
        (q.IncludeItemTypes.Length == 0 || q.IncludeItemTypes.Contains(Kind(i))) &&
        (q.AncestorIds.Length == 0 || (i is Episode e && q.AncestorIds.Contains(e.SeriesId)))).ToList();
    static BaseItemKind Kind(BaseItem i) => i switch { Episode => BaseItemKind.Episode, Season => BaseItemKind.Season, Series => BaseItemKind.Series, Movie => BaseItemKind.Movie, _ => BaseItemKind.Folder };
    public void QueueLibraryScan() => ScansQueued++;
    public void DeleteItem(BaseItem item, DeleteOptions o, bool notify)
    {
        if (o.DeleteFileLocation)
        {
            var p = item is Movie m && !m.IsInMixedFolder ? m.ContainingFolderPath : item.Path;
            if (Directory.Exists(p)) Directory.Delete(p, true); else if (File.Exists(p)) File.Delete(p);
        }
        Items.Remove(item);
    }
}
