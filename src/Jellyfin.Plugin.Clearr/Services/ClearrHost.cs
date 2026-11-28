using System.Text.Json;
using System.Text.Json.Nodes;
using Clearr.Core;
using Clearr.Storage;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Clearr.Services;

/// <summary>Wires Clearr.Core into Jellyfin: stores in &lt;data&gt;/clearr, the internal library source and the Jellyfin fallback deleter.</summary>
public class ClearrHost
{
    public ClearrHost(IApplicationPaths appPaths, ILibraryManager library, IUserManager users, IUserDataManager userData, IHttpClientFactory http, ILoggerFactory logs)
    {
        Log = logs.CreateLogger("Clearr");
        Paths = new ConfigPaths(Path.Combine(appPaths.DataPath, "clearr"));
        Settings = new SettingsStore(Paths);
        Keep = new KeepStore(Paths);
        History = new HistoryStore(Paths);
        Users = users;
        Migrate(Path.Combine(appPaths.DataPath, "watchtracker"));

        Engine = new ClearrEngine(http, Settings.Get, new JellyfinInternalSource(library, users, userData), Keep, History, Log, new JellyfinDirectDeleter(library));
    }

    public ILogger Log { get; }

    public ConfigPaths Paths { get; }

    public SettingsStore Settings { get; }

    public KeepStore Keep { get; }

    public HistoryStore History { get; }

    public IUserManager Users { get; }

    public ClearrEngine Engine { get; }

    public DateTime? LastRunAt { get; set; }

    public Msg? LastRunSummary { get; set; }

    /// <summary>One-time move from Watch Tracker: settings (from the plugin XML), keep list and history.</summary>
    private void Migrate(string oldDir)
    {
        if (File.Exists(Paths.File("settings.json")))
        {
            return;
        }

        var s = new ClearrSettings();
        var old = Plugin.Instance?.Configuration;
        if (old != null)
        {
            s.SetModes(Enum.TryParse<CleanupMode>(old.Mode, true, out var m) ? m : CleanupMode.DryRun);
            s.DeleteSeasons = old.DeleteSeasons;
            s.DeleteShows = old.DeleteSeries;
            s.RequireEndedForShowDelete = old.RequireEndedForSeriesDelete;
            s.ProtectLatestSeasonOfContinuing = old.ProtectLatestSeasonOfContinuing;
            s.GraceDays = old.GraceDays;
            s.MaxDeleteGbPerRun = old.MaxDeleteGbPerRun;
            s.KeepTag = string.IsNullOrWhiteSpace(old.KeepTag) ? "keep" : old.KeepTag;
            s.IncludeSpecials = old.IncludeSpecials;
            s.InactiveDays = old.InactiveDays;
            s.SampledMaxEpisodes = old.SampledMaxEpisodes;
            s.AbandonedUsersBlockCleanup = old.AbandonedUsersBlockCleanup;
            s.ExcludedUserIds = (old.ExcludedUserIds ?? Array.Empty<string>()).Select(x => Guid.TryParse(x, out var g) ? JellyfinInternalSource.Key(g) : x).ToList();
            s.Sonarr.Url = old.SonarrUrl ?? "";
            s.Sonarr.ApiKey = old.SonarrApiKey ?? "";
        }

        try
        {
            var keepFile = Path.Combine(oldDir, "keep.json");
            if (File.Exists(keepFile))
            {
                foreach (var g in JsonSerializer.Deserialize<List<Guid>>(File.ReadAllText(keepFile)) ?? new())
                {
                    Keep.Set(JellyfinInternalSource.Key(g), true);
                }
            }

            var histFile = Path.Combine(oldDir, "history.json");
            if (File.Exists(histFile) && JsonNode.Parse(File.ReadAllText(histFile)) is JsonArray arr)
            {
                History.Add(arr.Where(h => h != null).Select(h => new HistoryEntry
                {
                    Date = h!["date"]?.GetValue<DateTime>() ?? DateTime.UtcNow,
                    Action = h["action"]?.GetValue<string>() ?? "",
                    Kind = h["kind"]?.GetValue<string>() == "Series" ? "Show" : h["kind"]?.GetValue<string>() ?? "",
                    Title = h["seriesName"]?.GetValue<string>() ?? "",
                    SeasonName = h["seasonName"]?.GetValue<string>(),
                    Detail = h["detail"]?.GetValue<string>() ?? "",
                    Bytes = h["bytes"]?.GetValue<long>() ?? 0,
                }).ToList());
            }
        }
        catch (Exception ex)
        {
            Log.LogWarning(ex, "Clearr: couldn't migrate Watch Tracker keep list/history");
        }

        Settings.Save(s);
        Log.LogInformation("Clearr: settings created (migrated from Watch Tracker where available)");
    }
}
