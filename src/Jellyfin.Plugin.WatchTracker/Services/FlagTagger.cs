using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.WatchTracker.Configuration;
using Jellyfin.Plugin.WatchTracker.Models;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.WatchTracker.Services;

/// <summary>
/// Mirrors flags onto Jellyfin tags on the series ("wt:stalled:Anna", "wt:cleanup-ready"),
/// so they can be filtered/browsed in any Jellyfin client. Removes its own tags when disabled.
/// </summary>
public class FlagTagger
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<FlagTagger> _logger;

    public FlagTagger(ILibraryManager libraryManager, ILogger<FlagTagger> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    public async Task<int> ApplyAsync(WatchReport report, PluginConfiguration config, CancellationToken ct)
    {
        var prefix = (config.FlagTagPrefix ?? string.Empty).Trim();
        if (prefix.Length < 2)
        {
            // Too short a prefix would risk touching the user's own tags.
            return 0;
        }

        var changed = 0;
        foreach (var sr in report.Series)
        {
            ct.ThrowIfCancellationRequested();
            var item = _libraryManager.GetItemById(sr.SeriesId);
            if (item is null)
            {
                continue;
            }

            var desired = new List<string>();
            if (config.ApplyFlagTags)
            {
                foreach (var u in sr.Users)
                {
                    foreach (var f in u.Flags)
                    {
                        desired.Add($"{prefix}{f.ToLowerInvariant()}:{u.UserName}");
                    }
                }

                if (sr.Eligible || sr.Seasons.Any(s => s.Eligible))
                {
                    desired.Add(prefix + "cleanup-ready");
                }
            }

            var current = item.Tags ?? Array.Empty<string>();
            var kept = current.Where(t => !t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            var next = kept.Concat(desired).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

            if (!next.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).SequenceEqual(current.OrderBy(t => t, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase))
            {
                item.Tags = next;
                try
                {
                    await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, ct).ConfigureAwait(false);
                    changed++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Watch Tracker: could not update tags on {Name}", sr.Name);
                }
            }
        }

        return changed;
    }
}
