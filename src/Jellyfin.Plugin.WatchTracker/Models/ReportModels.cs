using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.WatchTracker.Models;

public static class WatchStatus
{
    public const string NotStarted = "NotStarted";
    public const string InProgress = "InProgress";
    public const string Finished = "Finished";
}

public static class WatchFlag
{
    /// <summary>Watched only a few episodes, then went quiet.</summary>
    public const string Sampled = "Sampled";

    /// <summary>Got partway through, then went quiet.</summary>
    public const string Stalled = "Stalled";

    /// <summary>Their last activity was stopping in the middle of an episode.</summary>
    public const string MidEpisode = "MidEpisode";
}

public class UserProgress
{
    public Guid UserId { get; set; }

    public string UserName { get; set; } = string.Empty;

    public string Status { get; set; } = WatchStatus.NotStarted;

    public int Watched { get; set; }

    public int Partial { get; set; }

    public int Total { get; set; }

    public DateTime? LastActivity { get; set; }

    public DateTime? FinishedAt { get; set; }

    /// <summary>e.g. "S02E05" — the episode they last touched.</summary>
    public string? LastEpisode { get; set; }

    public List<string> Flags { get; set; } = new();

    public bool IsAbandoned => Flags.Contains(WatchFlag.Sampled) || Flags.Contains(WatchFlag.Stalled);
}

public class SeasonReport
{
    public Guid SeasonId { get; set; }

    public string Name { get; set; } = string.Empty;

    public int? Number { get; set; }

    public int EpisodeCount { get; set; }

    public List<UserProgress> Users { get; set; } = new();

    public bool Eligible { get; set; }

    public string Reason { get; set; } = string.Empty;
}

public class SeriesReport
{
    public Guid SeriesId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Status { get; set; }

    public bool Kept { get; set; }

    public string? TvdbId { get; set; }

    public int EpisodeCount { get; set; }

    public List<UserProgress> Users { get; set; } = new();

    public List<SeasonReport> Seasons { get; set; } = new();

    public bool Eligible { get; set; }

    public string Reason { get; set; } = string.Empty;
}

public class WatchReport
{
    public DateTime GeneratedAt { get; set; }

    public List<string> TrackedUsers { get; set; } = new();

    public List<SeriesReport> Series { get; set; } = new();
}

public class HistoryEntry
{
    public DateTime Date { get; set; }

    /// <summary>Deleted, WouldDelete, Failed, Skipped.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Season or Series.</summary>
    public string Kind { get; set; } = string.Empty;

    public string SeriesName { get; set; } = string.Empty;

    public string? SeasonName { get; set; }

    public string Detail { get; set; } = string.Empty;
}

public class CleanupResult
{
    public bool DryRun { get; set; }

    public List<HistoryEntry> Actions { get; set; } = new();
}
