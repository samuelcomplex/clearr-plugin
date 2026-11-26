using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Clearr.Configuration;

/// <summary>
/// Clearr keeps its real settings in &lt;data&gt;/clearr/settings.json (same format as the Clearr app).
/// The fields below are the old Watch Tracker settings, kept only so they can be read once and migrated.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    public string Mode { get; set; } = "DryRun";
    public bool DeleteSeasons { get; set; } = true;
    public bool DeleteSeries { get; set; } = true;
    public bool RequireEndedForSeriesDelete { get; set; } = true;
    public bool ProtectLatestSeasonOfContinuing { get; set; } = true;
    public int GraceDays { get; set; } = 7;
    public int MaxDeleteGbPerRun { get; set; } = 50;
    public string KeepTag { get; set; } = "keep";
    public bool IncludeSpecials { get; set; }
    public int InactiveDays { get; set; } = 30;
    public int SampledMaxEpisodes { get; set; } = 3;
    public bool AbandonedUsersBlockCleanup { get; set; } = true;
    public string[] ExcludedUserIds { get; set; } = Array.Empty<string>();
    public string SonarrUrl { get; set; } = string.Empty;
    public string SonarrApiKey { get; set; } = string.Empty;
}
