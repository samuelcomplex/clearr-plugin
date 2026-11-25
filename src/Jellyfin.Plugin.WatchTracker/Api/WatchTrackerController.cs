using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.WatchTracker.Configuration;
using Jellyfin.Plugin.WatchTracker.Models;
using Jellyfin.Plugin.WatchTracker.Services;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.WatchTracker.Api;

/// <summary>Admin-only API used by the plugin page.</summary>
[ApiController]
[Route("WatchTracker")]
[Authorize(Policy = "RequiresElevation")]
public class WatchTrackerController : ControllerBase
{
    private readonly WatchAnalyzer _analyzer;
    private readonly CleanupService _cleanup;
    private readonly HistoryStore _history;
    private readonly SonarrClient _sonarr;
    private readonly KeepStore _keep;
    private readonly ILibraryManager _libraryManager;

    public WatchTrackerController(WatchAnalyzer analyzer, CleanupService cleanup, HistoryStore history, SonarrClient sonarr, KeepStore keep, ILibraryManager libraryManager)
    {
        _keep = keep;
        _libraryManager = libraryManager;
        _analyzer = analyzer;
        _cleanup = cleanup;
        _history = history;
        _sonarr = sonarr;
    }

    [HttpGet("Report")]
    public ActionResult GetReport() => Json(_analyzer.BuildReport());

    [HttpGet("History")]
    public ActionResult GetHistory() => Json(_history.GetAll());

    /// <summary>dryRun=true previews. dryRun=false deletes now, but ONLY when the mode is set to Delete.</summary>
    [HttpPost("Cleanup")]
    public async Task<ActionResult> RunCleanup([FromQuery] bool dryRun = true, CancellationToken ct = default)
    {
        var mode = Plugin.Instance?.Configuration.Mode ?? CleanupMode.DryRun;
        if (!dryRun && mode != CleanupMode.Delete)
        {
            var refused = Json(new CleanupResult { DryRun = true, Error = $"Nothing was deleted: cleanup mode is {mode}. Set the mode to Delete and save before deleting." });
            refused.StatusCode = 409;
            return refused;
        }

        var result = await _cleanup.RunAsync(dryRun, record: !dryRun, null, ct).ConfigureAwait(false);
        return Json(result);
    }

    /// <summary>Mark a show or season as keep (never deleted), or remove the mark. Also adds/removes the keep tag in Jellyfin.</summary>
    [HttpPost("Keep")]
    public async Task<ActionResult> SetKeep([FromQuery] Guid itemId, [FromQuery] bool keep, CancellationToken ct)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item is not Series && item is not Season)
        {
            var bad = Json(new { ok = false, message = "Not a show or season" });
            bad.StatusCode = 404;
            return bad;
        }

        _keep.Set(item.Id, keep);

        var tag = (Plugin.Instance?.Configuration.KeepTag ?? "keep").Trim();
        if (tag.Length > 0)
        {
            var tags = (item.Tags ?? Array.Empty<string>()).Where(t => !string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)).ToList();
            if (keep)
            {
                tags.Add(tag);
            }

            if (tags.Count != (item.Tags?.Length ?? 0))
            {
                item.Tags = tags.ToArray();
                await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, ct).ConfigureAwait(false);
            }
        }

        return Json(new { ok = true, kept = keep });
    }

    [HttpPost("TestSonarr")]
    public async Task<ActionResult> TestSonarr([FromBody] SonarrTestRequest? body, CancellationToken ct)
    {
        // Test what's typed in the form; fall back to saved settings.
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var url = string.IsNullOrWhiteSpace(body?.Url) ? config.SonarrUrl : body!.Url;
        var key = string.IsNullOrWhiteSpace(body?.ApiKey) ? config.SonarrApiKey : body!.ApiKey;
        try
        {
            return Json(new { ok = true, message = await _sonarr.TestAsync(url, key, ct).ConfigureAwait(false) });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, message = ex.Message });
        }
    }

    public class SonarrTestRequest
    {
        public string? Url { get; set; }

        public string? ApiKey { get; set; }
    }

    // Own serializer so the page always gets camelCase regardless of server JSON settings.
    private ContentResult Json(object value) =>
        Content(JsonSerializer.Serialize(value, JsonOpts.Options), "application/json");
}
