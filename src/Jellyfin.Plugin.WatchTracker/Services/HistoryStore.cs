using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.WatchTracker.Models;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.WatchTracker.Services;

/// <summary>Keeps a JSON log of cleanup actions in &lt;data&gt;/watchtracker/history.json.</summary>
public class HistoryStore
{
    private const int MaxEntries = 1000;
    private readonly object _lock = new();
    private readonly string _path;
    private readonly ILogger<HistoryStore> _logger;

    public HistoryStore(IApplicationPaths paths, ILogger<HistoryStore> logger)
    {
        _logger = logger;
        var dir = Path.Combine(paths.DataPath, "watchtracker");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "history.json");
    }

    public List<HistoryEntry> GetAll()
    {
        lock (_lock)
        {
            return Load();
        }
    }

    public void Add(IEnumerable<HistoryEntry> entries)
    {
        lock (_lock)
        {
            var list = Load();
            list.InsertRange(0, entries);
            if (list.Count > MaxEntries)
            {
                list = list.Take(MaxEntries).ToList();
            }

            try
            {
                File.WriteAllText(_path, JsonSerializer.Serialize(list, JsonOpts.Options));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Watch Tracker: could not write {Path}", _path);
            }
        }
    }

    private List<HistoryEntry> Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                return JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(_path), JsonOpts.Options) ?? new();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Watch Tracker: could not read {Path}", _path);
        }

        return new();
    }
}

internal static class JsonOpts
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
}
