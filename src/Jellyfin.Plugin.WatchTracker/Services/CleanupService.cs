using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.WatchTracker.Configuration;
using Jellyfin.Plugin.WatchTracker.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.WatchTracker.Services;

/// <summary>Deletes (or pretends to delete) eligible shows and seasons.</summary>
public class CleanupService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly WatchAnalyzer _analyzer;
    private readonly ILibraryManager _libraryManager;
    private readonly SonarrClient _sonarr;
    private readonly HistoryStore _history;
    private readonly FlagTagger _tagger;
    private readonly ILogger<CleanupService> _logger;

    public CleanupService(
        WatchAnalyzer analyzer,
        ILibraryManager libraryManager,
        SonarrClient sonarr,
        HistoryStore history,
        FlagTagger tagger,
        ILogger<CleanupService> logger)
    {
        _analyzer = analyzer;
        _libraryManager = libraryManager;
        _sonarr = sonarr;
        _history = history;
        _tagger = tagger;
        _logger = logger;
    }

    /// <param name="dryRun">true = only report; false = really delete.</param>
    /// <param name="record">Write the result to the history log.</param>
    public async Task<CleanupResult> RunAsync(bool dryRun, bool record, IProgress<double>? progress, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
            var report = _analyzer.BuildReport();
            var result = new CleanupResult { DryRun = dryRun };

            var work = new List<(SeriesReport Series, SeasonReport? Season)>();
            foreach (var s in report.Series)
            {
                if (config.DeleteSeries && s.Eligible)
                {
                    work.Add((s, null));
                    continue;
                }

                if (config.DeleteSeasons)
                {
                    work.AddRange(s.Seasons.Where(x => x.Eligible).Select(x => (s, (SeasonReport?)x)));
                }
            }

            for (var i = 0; i < work.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var (series, season) = work[i];
                result.Actions.Add(await ProcessAsync(config, series, season, dryRun, ct).ConfigureAwait(false));
                progress?.Report(80.0 * (i + 1) / work.Count);
            }

            // Keep flag tags in sync (also strips our tags when the feature is turned off).
            {
                try
                {
                    var tagReport = work.Count > 0 && !dryRun ? _analyzer.BuildReport() : report;
                    await _tagger.ApplyAsync(tagReport, config, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Watch Tracker: tagging failed");
                }
            }

            if (record && result.Actions.Count > 0)
            {
                _history.Add(result.Actions);
            }

            progress?.Report(100);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<HistoryEntry> ProcessAsync(PluginConfiguration config, SeriesReport series, SeasonReport? season, bool dryRun, CancellationToken ct)
    {
        var entry = new HistoryEntry
        {
            Date = DateTime.UtcNow,
            Kind = season is null ? "Series" : "Season",
            SeriesName = series.Name,
            SeasonName = season?.Name,
            Detail = season?.Reason ?? series.Reason
        };

        if (dryRun)
        {
            entry.Action = "WouldDelete";
            return entry;
        }

        try
        {
            if (_libraryManager.GetItemById(series.SeriesId) is not Series seriesItem)
            {
                entry.Action = "Skipped";
                entry.Detail = "Show no longer in library";
                return entry;
            }

            string sonarrNote = "Sonarr not configured";
            if (SonarrClient.IsConfigured(config))
            {
                // If this throws we do NOT delete: otherwise Sonarr would just download it again.
                sonarrNote = await _sonarr.UnmonitorAsync(config, series.TvdbId, season?.Number, ct).ConfigureAwait(false);
            }

            if (season is null)
            {
                DeleteSeries(seriesItem);
            }
            else
            {
                DeleteSeason(seriesItem, season);
            }

            entry.Action = "Deleted";
            entry.Detail += " · " + sonarrNote;
            _logger.LogInformation("Watch Tracker deleted {Kind} {Series} {Season}", entry.Kind, entry.SeriesName, entry.SeasonName);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            entry.Action = "Failed";
            entry.Detail = ex.Message;
            _logger.LogError(ex, "Watch Tracker failed to delete {Kind} {Series} {Season}", entry.Kind, entry.SeriesName, entry.SeasonName);
        }

        return entry;
    }

    private void DeleteSeries(Series series)
    {
        var path = series.Path;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            throw new InvalidOperationException("Show folder not found: " + path);
        }

        // Refuse if any other show lives inside this folder (e.g. misdetected library root).
        var full = NormalizeDir(path);
        var others = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Series },
            Recursive = true,
            IsVirtualItem = false
        }).Where(s => s.Id != series.Id && !string.IsNullOrEmpty(s.Path) && NormalizeDir(s.Path).StartsWith(full, StringComparison.OrdinalIgnoreCase)).ToList();

        if (others.Count > 0)
        {
            throw new InvalidOperationException("Refusing to delete: folder also contains " + others[0].Name);
        }

        _libraryManager.DeleteItem(series, new DeleteOptions { DeleteFileLocation = true }, true);
    }

    private void DeleteSeason(Series series, SeasonReport seasonReport)
    {
        var season = _libraryManager.GetItemById(seasonReport.SeasonId) as Season;
        var seriesDir = string.IsNullOrEmpty(series.Path) ? null : NormalizeDir(series.Path);
        var seasonDir = season is null || string.IsNullOrEmpty(season.Path) ? null : NormalizeDir(season.Path);

        var hasOwnFolder = seriesDir != null && seasonDir != null
            && !string.Equals(seasonDir, seriesDir, StringComparison.OrdinalIgnoreCase)
            && seasonDir.StartsWith(seriesDir, StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(seasonDir);

        if (season != null && hasOwnFolder)
        {
            // Season has its own folder inside the show folder: remove the folder (episodes, subs, images, nfo).
            _libraryManager.DeleteItem(season, new DeleteOptions { DeleteFileLocation = true }, true);
            return;
        }

        // Flat layout (episodes directly in the show folder): delete episode files one by one.
        var episodes = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Episode },
            AncestorIds = new[] { series.Id },
            Recursive = true,
            IsVirtualItem = false
        }).OfType<Episode>().Where(e => e.SeasonId == seasonReport.SeasonId).ToList();

        foreach (var ep in episodes)
        {
            _libraryManager.DeleteItem(ep, new DeleteOptions { DeleteFileLocation = true }, true);
        }

        if (season != null)
        {
            _libraryManager.DeleteItem(season, new DeleteOptions { DeleteFileLocation = false }, true);
        }
    }

    private static string NormalizeDir(string p) =>
        Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
}
