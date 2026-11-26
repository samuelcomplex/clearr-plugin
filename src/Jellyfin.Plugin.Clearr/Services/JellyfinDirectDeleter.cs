using Clearr.Core;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Clearr.Services;

/// <summary>
/// The plugin-only fallback (Settings → "Also remove items Sonarr/Radarr don't manage"): deletes from disk through Jellyfin.
/// Same safeguards as Watch Tracker had: season folders only when they sit inside the show folder, otherwise episode by
/// episode; a show folder is never removed if another show lives inside it.
/// </summary>
public class JellyfinDirectDeleter : IDirectDeleter
{
    private readonly ILibraryManager _library;

    public JellyfinDirectDeleter(ILibraryManager library) => _library = library;

    private T Item<T>(string id)
        where T : BaseItem =>
        _library.GetItemById(Guid.Parse(id)) as T ?? throw new InvalidOperationException("No longer in the Jellyfin library");

    public long Measure(DirectTarget target)
    {
        try
        {
            switch (target.Kind)
            {
                case "Show":
                    return DirSize(Item<Series>(target.ItemId).Path);
                case "Season":
                    var series = Item<Series>(target.ItemId);
                    var season = target.SeasonId != null && Guid.TryParse(target.SeasonId, out var sid) ? _library.GetItemById(sid) as Season : null;
                    return season != null && OwnFolder(series, season) ? DirSize(season.Path) : SeasonEpisodes(series, target).Sum(e => FileSize(e.Path));
                case "Movie":
                    var movie = Item<Movie>(target.ItemId);
                    return movie.IsInMixedFolder ? FileSize(movie.Path) : DirSize(movie.ContainingFolderPath);
                default:
                    return 0;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return 0;
        }
    }

    public string Delete(DirectTarget target)
    {
        switch (target.Kind)
        {
            case "Show":
                var series = Item<Series>(target.ItemId);
                var path = series.Path;
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                {
                    throw new InvalidOperationException("Show folder not found: " + path);
                }

                var full = Norm(path);
                var other = _library.GetItemList(new InternalItemsQuery { IncludeItemTypes = new[] { BaseItemKind.Series }, Recursive = true, IsVirtualItem = false })
                    .FirstOrDefault(s => s.Id != series.Id && !string.IsNullOrEmpty(s.Path) && Norm(s.Path).StartsWith(full, StringComparison.OrdinalIgnoreCase));
                if (other != null)
                {
                    throw new InvalidOperationException("Refusing to delete: the folder also contains " + other.Name);
                }

                _library.DeleteItem(series, new DeleteOptions { DeleteFileLocation = true }, true);
                return "show folder deleted through Jellyfin (not managed by Sonarr)";

            case "Season":
                var show = Item<Series>(target.ItemId);
                var season = target.SeasonId != null && Guid.TryParse(target.SeasonId, out var sid) ? _library.GetItemById(sid) as Season : null;
                if (season != null && OwnFolder(show, season))
                {
                    _library.DeleteItem(season, new DeleteOptions { DeleteFileLocation = true }, true);
                    return "season folder deleted through Jellyfin (not managed by Sonarr)";
                }

                var eps = SeasonEpisodes(show, target);
                foreach (var ep in eps)
                {
                    _library.DeleteItem(ep, new DeleteOptions { DeleteFileLocation = true }, true);
                }

                if (season != null)
                {
                    _library.DeleteItem(season, new DeleteOptions { DeleteFileLocation = false }, true);
                }

                return $"{eps.Count} episode files deleted through Jellyfin (not managed by Sonarr)";

            case "Movie":
                _library.DeleteItem(Item<Movie>(target.ItemId), new DeleteOptions { DeleteFileLocation = true }, true);
                return "movie deleted through Jellyfin (not managed by Radarr)";

            default:
                throw new InvalidOperationException("Unknown kind " + target.Kind);
        }
    }

    private List<Episode> SeasonEpisodes(Series series, DirectTarget target)
    {
        var episodes = _library.GetItemList(new InternalItemsQuery { IncludeItemTypes = new[] { BaseItemKind.Episode }, AncestorIds = new[] { series.Id }, Recursive = true, IsVirtualItem = false })
            .OfType<Episode>();
        return target.SeasonId != null && Guid.TryParse(target.SeasonId, out var sid)
            ? episodes.Where(e => e.SeasonId == sid).ToList()
            : episodes.Where(e => e.ParentIndexNumber == target.SeasonNumber).ToList();
    }

    private static bool OwnFolder(Series series, Season season)
    {
        if (string.IsNullOrEmpty(series.Path) || string.IsNullOrEmpty(season.Path))
        {
            return false;
        }

        var a = Norm(series.Path);
        var b = Norm(season.Path);
        return !string.Equals(a, b, StringComparison.OrdinalIgnoreCase) && b.StartsWith(a, StringComparison.OrdinalIgnoreCase) && Directory.Exists(b);
    }

    private static string Norm(string p) => Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

    private static long DirSize(string? p) => string.IsNullOrEmpty(p) || !Directory.Exists(p) ? 0 : new DirectoryInfo(p).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);

    private static long FileSize(string? p) => string.IsNullOrEmpty(p) || !File.Exists(p) ? 0 : new FileInfo(p).Length;
}
