using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.WatchTracker.Services;

/// <summary>
/// Shows/seasons marked "keep" from the plugin page, stored in &lt;data&gt;/watchtracker/keep.json.
/// Kept separately from Jellyfin tags so a "Replace all metadata" refresh can't silently remove protection.
/// </summary>
public class KeepStore
{
    private readonly object _lock = new();
    private readonly string _path;
    private readonly ILogger<KeepStore> _logger;
    private HashSet<Guid>? _ids;

    public KeepStore(IApplicationPaths paths, ILogger<KeepStore> logger)
    {
        _logger = logger;
        var dir = Path.Combine(paths.DataPath, "watchtracker");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "keep.json");
    }

    public bool Contains(Guid id)
    {
        lock (_lock)
        {
            return Load().Contains(id);
        }
    }

    public void Set(Guid id, bool keep)
    {
        lock (_lock)
        {
            var ids = Load();
            if (keep ? ids.Add(id) : ids.Remove(id))
            {
                try
                {
                    File.WriteAllText(_path, JsonSerializer.Serialize(ids.ToList()));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Watch Tracker: could not write {Path}", _path);
                    throw;
                }
            }
        }
    }

    private HashSet<Guid> Load()
    {
        if (_ids != null)
        {
            return _ids;
        }

        try
        {
            _ids = File.Exists(_path)
                ? new HashSet<Guid>(JsonSerializer.Deserialize<List<Guid>>(File.ReadAllText(_path)) ?? new())
                : new HashSet<Guid>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Watch Tracker: could not read {Path}", _path);
            _ids = new HashSet<Guid>();
        }

        return _ids;
    }
}
