using Clearr.Core;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Clearr.Services;

/// <summary>Dashboard → Scheduled Tasks → Clearr → "Clear finished shows and movies".</summary>
public class CleanupTask : IScheduledTask
{
    private readonly ClearrHost _host;

    public CleanupTask(ClearrHost host) => _host = host;

    public string Name => "Clear finished shows and movies";

    public string Key => "ClearrCleanup";

    public string Description => "Removes what everyone who started it has finished (or logs what would be removed, in Dry run mode).";

    public string Category => "Clearr";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var mode = _host.Settings.Get().Mode;
        if (mode == CleanupMode.Off)
        {
            progress.Report(100);
            return;
        }

        var r = await _host.Engine.CleanupAsync(dryRun: mode != CleanupMode.Delete, record: true, cancellationToken).ConfigureAwait(false);
        _host.LastRunAt = DateTime.UtcNow;
        _host.LastRunSummary = $"{(r.DryRun ? "Dry run" : "Cleanup")}: {r.Actions.Count(a => a.Action is "Deleted" or "WouldDelete")} item(s), {r.TotalBytes / 1073741824.0:0.0} GB";
        progress.Report(100);
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => new[]
    {
        new TaskTriggerInfo { Type = TaskTriggerInfoType.DailyTrigger, TimeOfDayTicks = TimeSpan.FromHours(4).Ticks },
    };
}
