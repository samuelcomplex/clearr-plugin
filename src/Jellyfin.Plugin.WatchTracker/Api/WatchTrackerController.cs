using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.WatchTracker.Configuration;
using Jellyfin.Plugin.WatchTracker.Services;
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

    public WatchTrackerController(WatchAnalyzer analyzer, CleanupService cleanup, HistoryStore history, SonarrClient sonarr)
    {
        _analyzer = analyzer;
        _cleanup = cleanup;
        _history = history;
        _sonarr = sonarr;
    }

    [HttpGet("Report")]
    public ActionResult GetReport() => Json(_analyzer.BuildReport());

    [HttpGet("History")]
    public ActionResult GetHistory() => Json(_history.GetAll());

    /// <summary>dryRun=true previews; dryRun=false deletes now (regardless of the scheduled mode).</summary>
    [HttpPost("Cleanup")]
    public async Task<ActionResult> RunCleanup([FromQuery] bool dryRun = true, CancellationToken ct = default)
    {
        var result = await _cleanup.RunAsync(dryRun, record: !dryRun, null, ct).ConfigureAwait(false);
        return Json(result);
    }

    [HttpPost("TestSonarr")]
    public async Task<ActionResult> TestSonarr(CancellationToken ct)
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        try
        {
            return Json(new { ok = true, message = await _sonarr.TestAsync(config, ct).ConfigureAwait(false) });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, message = ex.Message });
        }
    }

    // Own serializer so the page always gets camelCase regardless of server JSON settings.
    private ContentResult Json(object value) =>
        Content(JsonSerializer.Serialize(value, JsonOpts.Options), "application/json");
}
