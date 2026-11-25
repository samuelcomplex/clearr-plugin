using System;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.WatchTracker.Configuration;

/// <summary>What the scheduled cleanup task does.</summary>
public enum CleanupMode
{
    /// <summary>Never delete; the report still works.</summary>
    Off = 0,

    /// <summary>Log what would be deleted to the history, delete nothing.</summary>
    DryRun = 1,

    /// <summary>Actually delete eligible seasons/shows.</summary>
    Delete = 2
}

/// <summary>Plugin settings (stored as XML by Jellyfin).</summary>
public class PluginConfiguration : BasePluginConfiguration
{
    // ---- Cleanup ----
    public CleanupMode Mode { get; set; } = CleanupMode.DryRun;

    public bool DeleteSeasons { get; set; } = true;

    public bool DeleteSeries { get; set; } = true;

    /// <summary>Only delete a whole show when its status is Ended.</summary>
    public bool RequireEndedForSeriesDelete { get; set; } = true;

    /// <summary>Never delete the newest season of a show that is still Continuing (it may still be airing).</summary>
    public bool ProtectLatestSeasonOfContinuing { get; set; } = true;

    /// <summary>Days to wait after the last viewer finished before deleting.</summary>
    public int GraceDays { get; set; } = 7;

    /// <summary>Most data one run may delete, in GB (0 = no limit). The rest waits for the next run.</summary>
    public int MaxDeleteGbPerRun { get; set; } = 50;

    /// <summary>Tag on a series that blocks all cleanup for it (case-insensitive).</summary>
    public string KeepTag { get; set; } = "keep";

    /// <summary>Count season 0 / specials as part of a show.</summary>
    public bool IncludeSpecials { get; set; } = false;

    // ---- Flags ----

    /// <summary>A viewer with an unfinished show and no activity for this many days gets flagged.</summary>
    public int InactiveDays { get; set; } = 30;

    /// <summary>Flagged viewers who watched at most this many episodes are "Sampled" (vs "Stalled").</summary>
    public int SampledMaxEpisodes { get; set; } = 3;

    /// <summary>When false, flagged (sampled/stalled) viewers no longer hold back cleanup.</summary>
    public bool AbandonedUsersBlockCleanup { get; set; } = true;

    /// <summary>Write flags as Jellyfin tags on the series (e.g. "wt:stalled:Anna").</summary>
    public bool ApplyFlagTags { get; set; } = false;

    public string FlagTagPrefix { get; set; } = "wt:";

    /// <summary>Users that are ignored completely (Guid strings).</summary>
    public string[] ExcludedUserIds { get; set; } = Array.Empty<string>();

    // ---- Sonarr ----
    public string SonarrUrl { get; set; } = string.Empty;

    public string SonarrApiKey { get; set; } = string.Empty;

    /// <summary>Unmonitor the season/show in Sonarr before deleting, so it isn't re-downloaded.</summary>
    public bool UnmonitorInSonarr { get; set; } = true;
}
