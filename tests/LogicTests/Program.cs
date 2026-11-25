using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.WatchTracker.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Jellyfin.Data.Enums;

var root = Path.Combine(Path.GetTempPath(), "wt-test-" + Guid.NewGuid().ToString("N")[..6]);
var lib = new FakeLib(); var users = new FakeUsers(); var data = new FakeData();
User A = users.Add("Anna"), B = users.Add("Bjorn"), C = users.Add("Cleo");
var now = DateTime.UtcNow;

// Show 1: Ended, S1+S2 in own folders. Anna finished all 20d ago. Bjorn finished S1, mid S2 yesterday. Cleo watched 1 ep 60d ago.
var (s1, s1seasons, s1eps) = lib.AddSeries(root, "Ended Show", SeriesStatus.Ended, new[] { 3, 3 }, seasonFolders: true);
foreach (var e in s1eps) data.Set(A, e, played: true, at: now.AddDays(-20));
foreach (var e in s1eps.Where(e => e.ParentIndexNumber == 1)) data.Set(B, e, true, now.AddDays(-10));
data.Set(B, s1eps.First(e => e.ParentIndexNumber == 2), true, now.AddDays(-1));
data.Set(C, s1eps[0], true, now.AddDays(-60));

// Show 2: Continuing, flat layout. Anna finished S1 and S2 (latest). Bjorn stopped mid-episode in S1 45 days ago after 2 eps.
var (s2, _, s2eps) = lib.AddSeries(root, "Running Show", SeriesStatus.Continuing, new[] { 4, 2 }, seasonFolders: false);
foreach (var e in s2eps) data.Set(A, e, true, now.AddDays(-30));
// Bjorn did not start show 2 -> only Anna counts

// Show 3: Ended, everyone who started finished long ago -> whole show eligible. Has keep tag variant below.
var (s3, _, s3eps) = lib.AddSeries(root, "Done Show", SeriesStatus.Ended, new[] { 2 }, seasonFolders: true);
foreach (var e in s3eps) { data.Set(A, e, true, now.AddDays(-40)); data.Set(B, e, true, now.AddDays(-35)); }

// Show 4: same as 3 but kept
var (s4, _, s4eps) = lib.AddSeries(root, "Kept Show", SeriesStatus.Ended, new[] { 2 }, seasonFolders: true);
s4.Tags = new[] { "Keep" };
foreach (var e in s4eps) data.Set(A, e, true, now.AddDays(-40));

// Show 5: Bjorn sampled 2 eps 50d ago, stopped mid-episode on the 3rd.
var (s5, _, s5eps) = lib.AddSeries(root, "Sampled Show", SeriesStatus.Ended, new[] { 6 }, seasonFolders: true);
data.Set(B, s5eps[0], true, now.AddDays(-52)); data.Set(B, s5eps[1], true, now.AddDays(-51)); data.Set(B, s5eps[2], false, now.AddDays(-50), ticks: 12345);
foreach (var e in s5eps) data.Set(A, e, true, now.AddDays(-30));

var keepStore = new KeepStore(new FakePaths(root), NullLogger<KeepStore>.Instance);
var analyzer = new WatchAnalyzer(lib, users, data, keepStore);
var report = analyzer.BuildReport();
foreach (var s in report.Series)
{
    Console.WriteLine($"== {s.Name} [{s.Status}] eligible={s.Eligible} :: {s.Reason}");
    foreach (var u in s.Users) Console.WriteLine($"     {u.UserName,-6} {u.Status,-10} {u.Watched}/{u.Total} last={u.LastEpisode} flags=[{string.Join(",", u.Flags)}]");
    foreach (var x in s.Seasons) Console.WriteLine($"   - {x.Name}: eligible={x.Eligible} :: {x.Reason}");
}

System.IO.File.WriteAllText("/tmp/report.json", System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions{PropertyNamingPolicy=System.Text.Json.JsonNamingPolicy.CamelCase}));
int fails = 0;
void Expect(bool cond, string what) { Console.WriteLine((cond ? "PASS " : "FAIL ") + what); if (!cond) fails++; }
var r1 = report.Series.Single(s => s.Name == "Ended Show");
Expect(!r1.Eligible, "Ended Show not eligible as a whole (Bjorn mid S2, Cleo sampled)");
Expect(!r1.Seasons[0].Eligible && r1.Seasons[0].Reason.Contains("Cleo"), "Ended Show S1 blocked by Cleo (sampled, blocks by default)");
Expect(r1.Users.Single(u => u.UserName == "Cleo").Flags.Contains("Sampled"), "Cleo flagged Sampled");
Expect(r1.Users.Single(u => u.UserName == "Bjorn").Flags.Count == 0, "Bjorn not flagged (active)");
var r2 = report.Series.Single(s => s.Name == "Running Show");
Expect(r2.Seasons[0].Eligible, "Running Show S1 eligible");
Expect(!r2.Seasons[1].Eligible && r2.Seasons[1].Reason.Contains("continuing"), "Running Show latest season protected");
Expect(!r2.Eligible && r2.Reason.Contains("Continuing"), "Running Show whole-show delete blocked (Continuing)");
Expect(report.Series.Single(s => s.Name == "Done Show").Eligible, "Done Show eligible");
var r4 = report.Series.Single(s => s.Name == "Kept Show");
Expect(!r4.Eligible && r4.Reason.Contains("keep") && r4.Kept, "Kept Show protected by tag (case-insensitive)");
var b5 = report.Series.Single(s => s.Name == "Sampled Show").Users.Single(u => u.UserName == "Bjorn");
Expect(b5.Flags.Contains("Sampled") && b5.Flags.Contains("MidEpisode") && b5.LastEpisode == "S01E03", "Bjorn on Sampled Show: Sampled + MidEpisode at S01E03");
Expect(!report.Series.Single(s => s.Name == "Sampled Show").Eligible, "Sampled Show blocked while flagged viewers block");

// Keep via plugin: season-level on Running Show S1, then show-level on Done Show (then undo)
var ctlK = new Jellyfin.Plugin.WatchTracker.Api.WatchTrackerController(analyzer, null!, null!, new SonarrClient(null!), keepStore, lib);
var s2Season1 = report.Series.Single(x => x.Name == "Running Show").Seasons[0].SeasonId;
await ctlK.SetKeep(s2Season1, true, default);
var rk = analyzer.BuildReport();
var rs2 = rk.Series.Single(x => x.Name == "Running Show");
Expect(rs2.Seasons[0].Kept && !rs2.Seasons[0].Eligible && lib.GetItemById(s2Season1)!.Tags.Contains("keep"), "Keep season via plugin: kept, not eligible, tag written");
await ctlK.SetKeep(s3.Id, true, default);
s3.Tags = Array.Empty<string>(); // simulate a metadata refresh wiping tags
rk = analyzer.BuildReport();
Expect(!rk.Series.Single(x => x.Name == "Done Show").Eligible, "Keep survives tag wipe (stored by plugin)");
await ctlK.SetKeep(s3.Id, false, default);
await ctlK.SetKeep(s2Season1, false, default);
rk = analyzer.BuildReport();
Expect(rk.Series.Single(x => x.Name == "Done Show").Eligible && rk.Series.Single(x => x.Name == "Running Show").Seasons[0].Eligible && !lib.GetItemById(s2Season1)!.Tags.Contains("keep"), "Un-keep restores eligibility and removes tag");
var sk = (Microsoft.AspNetCore.Mvc.ContentResult)await ctlK.SetKeep(s1eps[0].Id, true, default);
Expect(sk.StatusCode == 404, "Keep rejects episodes");

// Season keep blocks whole-show deletion but not other seasons
var (s6, s6seasons, s6eps) = lib.AddSeries(root, "Two Season Show", SeriesStatus.Ended, new[] { 1, 1 }, seasonFolders: true);
foreach (var e in s6eps) data.Set(A, e, true, now.AddDays(-40));
await ctlK.SetKeep(s6seasons[1].Id, true, default);
var r6 = analyzer.BuildReport().Series.Single(x => x.Name == "Two Season Show");
Expect(!r6.Eligible && r6.Seasons[0].Eligible && !r6.Seasons[1].Eligible, "Kept S2 blocks whole-show delete; S1 still removable");

// Cleanup: dry run then real
var history = new HistoryStore(new FakePaths(root), NullLogger<HistoryStore>.Instance);
var cleanup = new CleanupService(analyzer, lib, new SonarrClient(null!), history, new FlagTagger(lib, NullLogger<FlagTagger>.Instance), NullLogger<CleanupService>.Instance);
var dry = await cleanup.RunAsync(true, false, null, default);
Console.WriteLine("Dry run: " + string.Join(" | ", dry.Actions.Select(a => $"{a.Action} {a.SeriesName} {a.SeasonName ?? "(show)"}")));
Expect(Directory.Exists(s3.Path), "Dry run deleted nothing");
// Guard: the API must refuse real deletion unless Mode == Delete
foreach (var m in new[] { Jellyfin.Plugin.WatchTracker.Configuration.CleanupMode.DryRun, Jellyfin.Plugin.WatchTracker.Configuration.CleanupMode.Off })
{
    _ = new TestPlugin(new Jellyfin.Plugin.WatchTracker.Configuration.PluginConfiguration { Mode = m });
    var ctl = new Jellyfin.Plugin.WatchTracker.Api.WatchTrackerController(analyzer, cleanup, history, new SonarrClient(null!), keepStore, lib);
    var res = (Microsoft.AspNetCore.Mvc.ContentResult)await ctl.RunCleanup(false, default);
    Console.WriteLine($"  {m}: HTTP {res.StatusCode} {res.Content}");
    Expect(res.StatusCode == 409 && Directory.Exists(s3.Path) && history.GetAll().Count == 0, $"API refuses to delete in {m} mode, nothing touched");
}
_ = new TestPlugin(new Jellyfin.Plugin.WatchTracker.Configuration.PluginConfiguration { Mode = Jellyfin.Plugin.WatchTracker.Configuration.CleanupMode.Delete });
var ctlDel = new Jellyfin.Plugin.WatchTracker.Api.WatchTrackerController(analyzer, cleanup, history, new SonarrClient(null!), keepStore, lib);
var prev = (Microsoft.AspNetCore.Mvc.ContentResult)await ctlDel.RunCleanup(true, default);
Expect(prev.StatusCode is null or 200 && prev.Content!.Contains("\"totalBytes\":") && Directory.Exists(s3.Path), "Preview in Delete mode reports sizes and deletes nothing");
Console.WriteLine("  preview: " + prev.Content![..Math.Min(160, prev.Content.Length)]);
var real = await cleanup.RunAsync(false, true, null, default);
Expect(real.TotalBytes > 0 && real.Actions.All(a => a.Bytes > 0), "Real run records sizes");
Console.WriteLine("Real:    " + string.Join(" | ", real.Actions.Select(a => $"{a.Action} {a.SeriesName} {a.SeasonName ?? "(show)"} :: {a.Detail}")));
Expect(!Directory.Exists(s3.Path), "Done Show folder deleted");
Expect(Directory.Exists(s4.Path), "Kept Show folder kept");
Expect(Directory.Exists(s1.Path) && s1eps.All(e => File.Exists(e.Path)), "Ended Show untouched");
Expect(!Directory.Exists(Path.Combine(s6.Path, "Season 1")) && Directory.Exists(Path.Combine(s6.Path, "Season 2")), "Two Season Show: S1 deleted, kept S2 survives");
var s2s1 = s2eps.Where(e => e.ParentIndexNumber == 1).ToList(); var s2s2 = s2eps.Where(e => e.ParentIndexNumber == 2).ToList();
Expect(s2s1.All(e => !File.Exists(e.Path)) && s2s2.All(e => File.Exists(e.Path)) && Directory.Exists(s2.Path), "Flat layout: only S1 episode files deleted, show folder kept");
Expect(history.GetAll().Count == real.Actions.Count, "History recorded");

// Abandoned viewers don't block -> Ended Show S1 now eligible; Sampled Show eligible
var cfg = new Jellyfin.Plugin.WatchTracker.Configuration.PluginConfiguration { AbandonedUsersBlockCleanup = false };
_ = new TestPlugin(cfg);
var r2b = analyzer.BuildReport();
var e1 = r2b.Series.Single(s => s.Name == "Ended Show"); var sp = r2b.Series.Single(s => s.Name == "Sampled Show");
Console.WriteLine("  " + e1.Seasons[0].Reason + " | " + sp.Reason);
Expect(e1.Seasons[0].Eligible && e1.Seasons[0].Reason.Contains("abandoned"), "Non-blocking: Ended Show S1 eligible, Cleo ignored");
Expect(!e1.Seasons[1].Eligible, "Non-blocking: S2 still waits on active Bjorn");
Expect(sp.Eligible, "Non-blocking: Sampled Show eligible");
Console.WriteLine(fails == 0 ? "\nALL PASSED" : $"\n{fails} FAILED");
Directory.Delete(root, true);
return fails;

class TestPlugin : Jellyfin.Plugin.WatchTracker.Plugin { public TestPlugin(Jellyfin.Plugin.WatchTracker.Configuration.PluginConfiguration c) : base(null!, null!) { Configuration = c; } }
class FakeUsers : IUserManager
{
    public List<User> All = new();
    public User Add(string n) { var u = new User { Id = Guid.NewGuid(), Username = n }; All.Add(u); return u; }
    public IEnumerable<User> GetUsers() => All;
    public User? GetUserById(Guid id) => All.FirstOrDefault(u => u.Id == id);
}
class FakeData : IUserDataManager
{
    Dictionary<(Guid, Guid), UserItemData> d = new();
    public void Set(User u, BaseItem i, bool played, DateTime at, long ticks = 0) => d[(u.Id, i.Id)] = new UserItemData { Played = played, LastPlayedDate = at, PlaybackPositionTicks = ticks };
    public UserItemData? GetUserData(User user, BaseItem item) => d.TryGetValue((user.Id, item.Id), out var x) ? x : new UserItemData();
}
class FakePaths : IApplicationPaths { public FakePaths(string r) { DataPath = Path.Combine(r, "_data"); } public string DataPath { get; } public string PluginConfigurationsPath => DataPath; }
class FakeLib : ILibraryManager
{
    public List<BaseItem> Items = new();
    public (Series, List<Season>, List<Episode>) AddSeries(string root, string name, SeriesStatus st, int[] seasons, bool seasonFolders)
    {
        var dir = Path.Combine(root, name); Directory.CreateDirectory(dir);
        var s = new Series { Id = Guid.NewGuid(), Name = name, Path = dir, Status = st, ProviderIds = new() { ["Tvdb"] = "123" } };
        Items.Add(s); var ss = new List<Season>(); var es = new List<Episode>();
        for (int n = 1; n <= seasons.Length; n++)
        {
            var sdir = seasonFolders ? Path.Combine(dir, $"Season {n}") : null;
            if (sdir != null) Directory.CreateDirectory(sdir);
            var se = new Season { Id = Guid.NewGuid(), Name = $"Season {n}", SeriesId = s.Id, IndexNumber = n, Path = sdir ?? "" };
            Items.Add(se); ss.Add(se);
            for (int e = 1; e <= seasons[n - 1]; e++)
            {
                var f = Path.Combine(sdir ?? dir, $"{name} S{n:00}E{e:00}.mkv"); File.WriteAllText(f, "x");
                var ep = new Episode { Id = Guid.NewGuid(), Name = $"Ep {e}", SeriesId = s.Id, SeasonId = se.Id, ParentIndexNumber = n, IndexNumber = e, Path = f };
                Items.Add(ep); es.Add(ep);
            }
        }
        return (s, ss, es);
    }
    public BaseItem? GetItemById(Guid id) => Items.FirstOrDefault(i => i.Id == id);
    public IReadOnlyList<BaseItem> GetItemList(InternalItemsQuery q) => Items.Where(i =>
        (q.IncludeItemTypes.Length == 0 || q.IncludeItemTypes.Contains(Kind(i))) &&
        (q.AncestorIds.Length == 0 || (i is Episode e && q.AncestorIds.Contains(e.SeriesId)))).ToList();
    static BaseItemKind Kind(BaseItem i) => i switch { Episode => BaseItemKind.Episode, Season => BaseItemKind.Season, Series => BaseItemKind.Series, _ => BaseItemKind.Folder };
    public void DeleteItem(BaseItem item, DeleteOptions o, bool notify)
    {
        if (o.DeleteFileLocation)
        {
            if (Directory.Exists(item.Path)) Directory.Delete(item.Path, true); else if (File.Exists(item.Path)) File.Delete(item.Path);
        }
        Items.Remove(item);
        if (item is Series s) Items.RemoveAll(i => (i is Episode e && e.SeriesId == s.Id) || (i is Season x && x.SeriesId == s.Id));
        if (item is Season se) Items.RemoveAll(i => i is Episode e && e.SeasonId == se.Id);
    }
}
