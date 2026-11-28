using System.Text.Json;
using System.Text.Json.Nodes;
using Clearr.Clients;
using Clearr.Core;
using Clearr.Storage;
using Jellyfin.Plugin.Clearr.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Clearr.Api;

/// <summary>
/// The same API the Clearr app serves under /api, so the shared Clearr UI works unchanged inside the dashboard.
/// Admin only. The UI files themselves (ui/…) are public, like any other static script.
/// </summary>
[ApiController]
[Route("Clearr")]
[Authorize(Policy = "RequiresElevation")]
public class ClearrController : ControllerBase
{
    private readonly ClearrHost _host;
    private readonly IHttpClientFactory _http;

    public ClearrController(ClearrHost host, IHttpClientFactory http)
    {
        _host = host;
        _http = http;
    }

    [HttpGet("ui/{file}")]
    [AllowAnonymous]
    public ActionResult Ui(string file)
    {
        var stream = ClearrUi.Open(file);
        return stream == null ? NotFound() : File(stream, ClearrUi.ContentType(file));
    }

    [HttpGet("status")]
    public ActionResult Status()
    {
        var s = _host.Settings.Get();
        return J(new
        {
            version = typeof(ClearrController).Assembly.GetName().Version?.ToString(3),
            configured = true,
            passwordRequired = false,
            authenticated = true,
            showsMode = s.ShowsMode,
            moviesMode = s.MoviesMode,
            language = s.Language,
            lastRunAt = _host.LastRunAt,
            lastRunSummary = _host.LastRunSummary,
            notice = _host.Settings.Notice,
        });
    }

    [HttpGet("report")]
    public async Task<ActionResult> Report(CancellationToken ct)
    {
        var built = await _host.Engine.BuildAsync(ct).ConfigureAwait(false);
        return J(built.Report);
    }

    [HttpPost("cleanup")]
    public async Task<ActionResult> Cleanup([FromQuery] bool dryRun = true, [FromQuery] string? scope = null, CancellationToken ct = default)
    {
        var r = await _host.Engine.CleanupAsync(dryRun, record: !dryRun, ct, CleanupScopes.Parse(scope)).ConfigureAwait(false);
        return J(r, r.Error != null ? 409 : 200);
    }

    [HttpGet("history")]
    public ActionResult History() => J(_host.Engine.History());

    [HttpPost("keep")]
    public async Task<ActionResult> Keep([FromBody] JsonObject body, CancellationToken ct)
    {
        var id = body["id"]?.GetValue<string>();
        var kind = body["kind"]?.GetValue<string>() ?? "show";
        if (string.IsNullOrWhiteSpace(id) || kind is not ("show" or "season" or "movie"))
        {
            return J(new { error = "id and kind (show, season, movie) are required" }, 400);
        }

        var msg = await _host.Engine.SetKeepAsync(id, kind, body["keep"]?.GetValue<bool>() ?? true, ct).ConfigureAwait(false);
        return J(new { ok = true, message = msg });
    }

    [HttpGet("settings")]
    public ActionResult GetSettings() => J(new { settings = _host.Settings.Get(), hasPassword = false, apiKey = (string?)null });

    [HttpPut("settings")]
    public ActionResult PutSettings([FromBody] JsonObject body)
    {
        var incoming = body["settings"].Deserialize<ClearrSettings>(Json.Options) ?? new ClearrSettings();
        var current = _host.Settings.Get();
        incoming.Jellyfin = current.Jellyfin;   // not used by the plugin
        incoming.Https = current.Https;         // not used by the plugin
        incoming.PasswordHash = current.PasswordHash;
        if (SettingsRules.Normalize(incoming) is string problem)
        {
            return J(new { error = problem }, 400);
        }

        _host.Settings.Save(incoming);
        return J(new { ok = true });
    }

    [HttpGet("summary")]
    public ActionResult Summary()
    {
        var s = _host.Settings.Get();
        var o = ClearrSummary.Build(_host.Engine.History(), s, DateTime.UtcNow);
        o["lastRunAt"] = _host.LastRunAt;
        o["lastRun"] = _host.LastRunSummary is Msg m ? Messages.Render(m, s.Language) : null;
        o["version"] = typeof(ClearrController).Assembly.GetName().Version?.ToString(3);
        return Content(o.ToJsonString(), "application/json");
    }

    [HttpGet("rootfolders")]
    public async Task<ActionResult> RootFolders(CancellationToken ct)
    {
        var (sonarr, radarr) = await _host.Engine.RootFoldersAsync(ct).ConfigureAwait(false);
        return J(new { sonarr, radarr });
    }

    [HttpPost("notifications/test")]
    public async Task<ActionResult> TestNotifications([FromBody] JsonObject? body, CancellationToken ct)
    {
        var n = body?.Deserialize<NotificationSettings>(Json.Options) ?? _host.Settings.Get().Notifications;
        if (!n.AnyTarget)
        {
            return J(new { error = "No notification target is set." }, 400);
        }

        var results = await _host.Engine.TestNotificationsAsync(n, ct).ConfigureAwait(false);
        return J(new { results = results.Select(r => new { target = r.Target, error = r.Error }) });
    }

    [HttpGet("backup")]
    public ActionResult GetBackup() =>
        J(Backup.Create(_host.Settings, _host.Keep, typeof(ClearrController).Assembly.GetName().Version?.ToString(3) ?? ""));

    [HttpPost("backup/restore")]
    public ActionResult RestoreBackup([FromBody] JsonNode? body)
    {
        var current = _host.Settings.Get();
        var problem = Backup.Restore(body, _host.Settings, _host.Keep, s =>
        {
            s.Jellyfin = current.Jellyfin;   // the plugin talks to Jellyfin directly
            s.Https = current.Https;
        });
        return problem == null ? J(new { ok = true }) : J(new { error = problem }, 400);
    }

    [HttpPost("test/{service}")]
    public async Task<ActionResult> Test(string service, [FromBody] ConnectionSettings c, CancellationToken ct)
    {
        try
        {
            var client = _http.CreateClient("clearr");
            client.Timeout = TimeSpan.FromSeconds(15);
            var msg = service.ToLowerInvariant() switch
            {
                "sonarr" => await new SonarrClient(client, c).TestAsync(ct).ConfigureAwait(false),
                "radarr" => await new RadarrClient(client, c).TestAsync(ct).ConfigureAwait(false),
                _ => throw new ArgumentException("Unknown service " + service),
            };
            return J(new { ok = true, message = msg });
        }
        catch (Exception ex) when (ex is ApiException or ArgumentException)
        {
            return J(new { ok = false, message = ex.Message });
        }
    }

    [HttpGet("users")]
    public ActionResult Users() => J(JellyfinCompat.AllUsers(_host.Users).Select(u => new { id = JellyfinInternalSource.Key(u.Id), name = u.Username }));

    private static ContentResult J(object value, int status = 200) => new()
    {
        Content = JsonSerializer.Serialize(value, Json.Options),
        ContentType = "application/json",
        StatusCode = status,
    };
}
