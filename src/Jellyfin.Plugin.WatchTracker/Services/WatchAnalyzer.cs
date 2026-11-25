using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.WatchTracker.Configuration;
using Jellyfin.Plugin.WatchTracker.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.WatchTracker.Services;

/// <summary>
/// Builds per-user progress for every show and season straight from Jellyfin's own
/// played/resume data, and decides what is eligible for cleanup.
/// </summary>
public class WatchAnalyzer
{
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly IUserDataManager _userDataManager;

    public WatchAnalyzer(ILibraryManager libraryManager, IUserManager userManager, IUserDataManager userDataManager)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _userDataManager = userDataManager;
    }

    private static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

    public WatchReport BuildReport()
    {
        var config = Config;
        var now = DateTime.UtcNow;

        var excluded = new HashSet<Guid>(
            (config.ExcludedUserIds ?? Array.Empty<string>())
                .Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty)
                .Where(g => g != Guid.Empty));

        var users = _userManager.GetUsers().Where(u => !excluded.Contains(u.Id)).ToList();

        var report = new WatchReport
        {
            GeneratedAt = now,
            TrackedUsers = users.Select(u => u.Username).ToList()
        };

        var allSeries = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Series },
            Recursive = true,
            IsVirtualItem = false
        }).OfType<Series>().ToList();

        var episodesBySeries = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Episode },
            Recursive = true,
            IsVirtualItem = false
        }).OfType<Episode>().GroupBy(e => e.SeriesId).ToDictionary(g => g.Key, g => g.ToList());

        var seasonsById = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Season },
            Recursive = true
        }).OfType<Season>().GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());

        foreach (var series in allSeries.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!episodesBySeries.TryGetValue(series.Id, out var episodes))
            {
                continue;
            }

            if (!config.IncludeSpecials)
            {
                episodes = episodes.Where(e => SeasonNumber(e, seasonsById) != 0).ToList();
            }

            if (episodes.Count == 0)
            {
                continue;
            }

            report.Series.Add(AnalyzeSeries(series, episodes, seasonsById, users, config, now));
        }

        return report;
    }

    private SeriesReport AnalyzeSeries(
        Series series,
        List<Episode> episodes,
        Dictionary<Guid, Season> seasonsById,
        List<Jellyfin.Database.Implementations.Entities.User> users,
        PluginConfiguration config,
        DateTime now)
    {
        var keepTag = (config.KeepTag ?? string.Empty).Trim();
        var sr = new SeriesReport
        {
            SeriesId = series.Id,
            Name = series.Name,
            Status = series.Status?.ToString(),
            Kept = keepTag.Length > 0 && (series.Tags ?? Array.Empty<string>()).Any(t => string.Equals(t, keepTag, StringComparison.OrdinalIgnoreCase)),
            TvdbId = GetTvdbId(series),
            EpisodeCount = episodes.Count
        };

        // Gather user data once: [user][episode]
        var seasonGroups = episodes
            .GroupBy(e => e.SeasonId)
            .Select(g => new
            {
                SeasonId = g.Key,
                Number = SeasonNumber(g.First(), seasonsById),
                Episodes = g.OrderBy(e => e.IndexNumber ?? int.MaxValue).ToList()
            })
            .OrderBy(g => g.Number ?? int.MaxValue)
            .ToList();

        var maxSeasonNumber = seasonGroups.Where(g => g.Number.HasValue && g.Number > 0).Select(g => g.Number!.Value).DefaultIfEmpty(-1).Max();

        foreach (var g in seasonGroups)
        {
            seasonsById.TryGetValue(g.SeasonId, out var season);
            var seasonReport = new SeasonReport
            {
                SeasonId = g.SeasonId,
                Number = g.Number,
                Name = season?.Name ?? (g.Number.HasValue ? "Season " + g.Number.Value.ToString(CultureInfo.InvariantCulture) : "Unknown season"),
                EpisodeCount = g.Episodes.Count
            };

            foreach (var user in users)
            {
                seasonReport.Users.Add(Measure(user, g.Episodes, seasonsById, config, now));
            }

            var isProtectedLatest = config.ProtectLatestSeasonOfContinuing
                && series.Status == SeriesStatus.Continuing
                && g.Number.HasValue
                && g.Number.Value == maxSeasonNumber;

            (seasonReport.Eligible, seasonReport.Reason) = Decide(
                seasonReport.Users, config, now, sr.Kept, isProtectedLatest ? "Newest season of a show that's still continuing" : null);

            sr.Seasons.Add(seasonReport);
        }

        foreach (var user in users)
        {
            sr.Users.Add(Measure(user, episodes, seasonsById, config, now));
        }

        string? seriesBlock = null;
        if (config.RequireEndedForSeriesDelete && series.Status != SeriesStatus.Ended)
        {
            seriesBlock = series.Status.HasValue ? "Show status is " + series.Status.Value : "Show status is unknown (not marked Ended)";
        }

        (sr.Eligible, sr.Reason) = Decide(sr.Users, config, now, sr.Kept, seriesBlock);
        return sr;
    }

    private UserProgress Measure(
        Jellyfin.Database.Implementations.Entities.User user,
        List<Episode> episodes,
        Dictionary<Guid, Season> seasonsById,
        PluginConfiguration config,
        DateTime now)
    {
        var p = new UserProgress { UserId = user.Id, UserName = user.Username, Total = episodes.Count };
        Episode? lastEpisode = null;
        bool lastWasPartial = false;

        foreach (var ep in episodes)
        {
            var data = _userDataManager.GetUserData(user, ep);
            if (data is null)
            {
                continue;
            }

            var played = data.Played;
            var partial = !played && data.PlaybackPositionTicks > 0;
            if (played)
            {
                p.Watched++;
            }
            else if (partial)
            {
                p.Partial++;
            }

            if ((played || partial) && data.LastPlayedDate.HasValue
                && (!p.LastActivity.HasValue || data.LastPlayedDate.Value >= p.LastActivity.Value))
            {
                p.LastActivity = data.LastPlayedDate.Value;
                lastEpisode = ep;
                lastWasPartial = partial;
            }
        }

        if (p.Watched == 0 && p.Partial == 0)
        {
            p.Status = WatchStatus.NotStarted;
            return p;
        }

        if (lastEpisode != null)
        {
            var s = SeasonNumber(lastEpisode, seasonsById);
            p.LastEpisode = string.Format(CultureInfo.InvariantCulture, "S{0:00}E{1:00}", s ?? 0, lastEpisode.IndexNumber ?? 0);
        }

        if (p.Watched == p.Total)
        {
            p.Status = WatchStatus.Finished;
            p.FinishedAt = p.LastActivity; // null => finished, date unknown (e.g. marked played long ago)
            return p;
        }

        p.Status = WatchStatus.InProgress;

        var inactive = !p.LastActivity.HasValue || (now - p.LastActivity.Value).TotalDays >= Math.Max(1, config.InactiveDays);
        if (inactive)
        {
            p.Flags.Add(p.Watched <= Math.Max(0, config.SampledMaxEpisodes) ? WatchFlag.Sampled : WatchFlag.Stalled);
            if (lastWasPartial)
            {
                p.Flags.Add(WatchFlag.MidEpisode);
            }
        }

        return p;
    }

    private static (bool Eligible, string Reason) Decide(
        List<UserProgress> users, PluginConfiguration config, DateTime now, bool kept, string? extraBlock)
    {
        var starters = users.Where(u => u.Status != WatchStatus.NotStarted).ToList();
        if (starters.Count == 0)
        {
            return (false, "Nobody has started it");
        }

        var counted = config.AbandonedUsersBlockCleanup ? starters : starters.Where(u => !u.IsAbandoned).ToList();
        var finished = counted.Where(u => u.Status == WatchStatus.Finished).ToList();
        var waiting = counted.Where(u => u.Status != WatchStatus.Finished).ToList();

        if (waiting.Count > 0)
        {
            return (false, "Waiting on " + string.Join(", ", waiting.Select(Describe)));
        }

        if (finished.Count == 0)
        {
            return (false, "Nobody has finished it (everyone who started is flagged as abandoned)");
        }

        if (kept)
        {
            return (false, "Everyone finished, but the show has the keep tag");
        }

        if (extraBlock != null)
        {
            return (false, "Everyone finished, but: " + extraBlock);
        }

        var lastFinish = finished.Where(u => u.FinishedAt.HasValue).Select(u => u.FinishedAt!.Value).DefaultIfEmpty(DateTime.MinValue).Max();
        var graceEnds = lastFinish == DateTime.MinValue ? DateTime.MinValue : lastFinish.AddDays(Math.Max(0, config.GraceDays));
        if (graceEnds > now)
        {
            return (false, "Everyone finished; grace period until " + graceEnds.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        var skipped = starters.Count - counted.Count;
        var who = string.Join(", ", finished.Select(u => u.UserName));
        return (true, "Finished by " + who + (skipped > 0 ? $" ({skipped} abandoned viewer(s) ignored)" : string.Empty));
    }

    private static string Describe(UserProgress u)
    {
        var s = $"{u.UserName} ({u.Watched}/{u.Total}";
        if (u.Flags.Count > 0)
        {
            s += ", " + string.Join(", ", u.Flags.Select(f => f == WatchFlag.MidEpisode ? "mid-episode" : f.ToLowerInvariant()));
        }

        return s + ")";
    }

    internal static int? SeasonNumber(Episode e, Dictionary<Guid, Season> seasonsById)
    {
        if (e.ParentIndexNumber.HasValue)
        {
            return e.ParentIndexNumber;
        }

        return seasonsById.TryGetValue(e.SeasonId, out var s) ? s.IndexNumber : null;
    }

    internal static string? GetTvdbId(BaseItem item)
    {
        var ids = item.ProviderIds;
        if (ids is null)
        {
            return null;
        }

        foreach (var kv in ids)
        {
            if (string.Equals(kv.Key, "Tvdb", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(kv.Value))
            {
                return kv.Value;
            }
        }

        return null;
    }
}
