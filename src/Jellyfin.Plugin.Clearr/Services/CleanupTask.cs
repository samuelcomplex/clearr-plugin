using Clearr.Core;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Clearr.Services;

/// <summary>
/// The daily runs, as Jellyfin scheduled tasks (Dashboard → Scheduled Tasks → Clearr): one for shows and one for movies,
/// so each can have its own schedule. Each follows its own cleanup mode.
/// </summary>
public abstract class CleanupTaskBase : IScheduledTask
{
    private readonly ClearrHost _host;
    private readonly CleanupScope _kind;

    protected CleanupTaskBase(ClearrHost host, CleanupScope kind)
    {
        _host = host;
        _kind = kind;
    }

    public abstract string Name { get; }

    public abstract string Key { get; }

    public abstract string Description { get; }

    public string Category => "Clearr";

    protected abstract TimeSpan DefaultTime { get; }

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        if (ClearrEngine.ScheduledDryRun(_host.Settings.Get().ModeFor(_kind)) is not bool dryRun)
        {
            progress.Report(100);
            return;
        }

        var r = await _host.Engine.CleanupAsync(dryRun, record: true, cancellationToken, _kind).ConfigureAwait(false);
        var name = _kind == CleanupScope.Movies ? "movies" : "shows";
        _host.LastRunAt = DateTime.UtcNow;
        _host.LastRunSummary = new Msg($"run.{name}{(r.DryRun ? "Dry" : "Cleanup")}", new()
        {
            ["items"] = r.Actions.Count(a => a.Action is "Deleted" or "WouldDelete"),
            ["gb"] = Math.Round(r.TotalBytes / 1073741824.0, 1),
        });
        progress.Report(100);
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => new[]
    {
        new TaskTriggerInfo { Type = TaskTriggerInfoType.DailyTrigger, TimeOfDayTicks = DefaultTime.Ticks },
    };
}

/// <summary>"Clear finished shows": seasons and whole shows, in the shows' cleanup mode.</summary>
public class ShowsCleanupTask : CleanupTaskBase
{
    public ShowsCleanupTask(ClearrHost host)
        : base(host, CleanupScope.Shows)
    {
    }

    public override string Name => "Clear finished shows";

    public override string Key => "ClearrShows";

    public override string Description => "Removes seasons and shows that everyone who started has finished (or logs what would be removed, in Dry run mode). Follows the shows' cleanup mode.";

    protected override TimeSpan DefaultTime => TimeSpan.FromHours(4);
}

/// <summary>"Clear finished movies", in the movies' cleanup mode.</summary>
public class MoviesCleanupTask : CleanupTaskBase
{
    public MoviesCleanupTask(ClearrHost host)
        : base(host, CleanupScope.Movies)
    {
    }

    public override string Name => "Clear finished movies";

    public override string Key => "ClearrMovies";

    public override string Description => "Removes movies that everyone who started has finished (or logs what would be removed, in Dry run mode). Follows the movies' cleanup mode.";

    protected override TimeSpan DefaultTime => TimeSpan.FromHours(4.5);
}
