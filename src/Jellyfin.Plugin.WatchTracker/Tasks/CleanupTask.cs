using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.WatchTracker.Configuration;
using Jellyfin.Plugin.WatchTracker.Services;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.WatchTracker.Tasks;

/// <summary>Dashboard → Scheduled Tasks → Watch Tracker → "Clean up finished shows".</summary>
public class CleanupTask : IScheduledTask
{
    private readonly CleanupService _cleanup;

    public CleanupTask(CleanupService cleanup)
    {
        _cleanup = cleanup;
    }

    public string Name => "Clean up finished shows";

    public string Key => "WatchTrackerCleanup";

    public string Description => "Removes seasons and shows that everyone who started has finished (or logs what would be removed, in dry-run mode). Also refreshes flag tags.";

    public string Category => "Watch Tracker";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var mode = Plugin.Instance?.Configuration.Mode ?? CleanupMode.DryRun;
        if (mode == CleanupMode.Off)
        {
            // Still keep tags in sync.
            await _cleanup.RunAsync(dryRun: true, record: false, progress, cancellationToken).ConfigureAwait(false);
            return;
        }

        await _cleanup.RunAsync(mode != CleanupMode.Delete, record: true, progress, cancellationToken).ConfigureAwait(false);
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return new[]
        {
            new TaskTriggerInfo { Type = TaskTriggerInfoType.DailyTrigger, TimeOfDayTicks = TimeSpan.FromHours(4).Ticks }
        };
    }
}
