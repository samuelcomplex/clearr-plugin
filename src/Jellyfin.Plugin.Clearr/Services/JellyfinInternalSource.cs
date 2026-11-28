using Clearr.Core;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Clearr.Services;

/// <summary>Builds Clearr's library snapshot straight from Jellyfin's internals (no API key needed).</summary>
public class JellyfinInternalSource : ILibrarySource
{
    private readonly ILibraryManager _library;
    private readonly IUserManager _users;
    private readonly IUserDataManager _userData;

    public JellyfinInternalSource(ILibraryManager library, IUserManager users, IUserDataManager userData)
    {
        _library = library;
        _users = users;
        _userData = userData;
    }

    /// <summary>Ids in the same 32-hex form Jellyfin's API uses, so keep lists match between app and plugin.</summary>
    public static string Key(Guid id) => id.ToString("N");

    public Task<LibrarySnapshot> LoadAsync(CancellationToken ct)
    {
        var users = JellyfinCompat.AllUsers(_users).ToList();
        var lib = new LibrarySnapshot { Users = users.Select(u => new MediaUser(Key(u.Id), u.Username)).ToList() };

        var series = _library.GetItemList(new InternalItemsQuery { IncludeItemTypes = new[] { BaseItemKind.Series }, Recursive = true, IsVirtualItem = false }).OfType<Series>().ToList();
        var seasons = _library.GetItemList(new InternalItemsQuery { IncludeItemTypes = new[] { BaseItemKind.Season }, Recursive = true }).OfType<Season>().GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
        var episodes = _library.GetItemList(new InternalItemsQuery { IncludeItemTypes = new[] { BaseItemKind.Episode }, Recursive = true, IsVirtualItem = false }).OfType<Episode>().ToList();
        var movies = _library.GetItemList(new InternalItemsQuery { IncludeItemTypes = new[] { BaseItemKind.Movie }, Recursive = true, IsVirtualItem = false }).OfType<Movie>().ToList();

        var byShow = episodes.GroupBy(e => e.SeriesId).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var s in series)
        {
            var show = new ShowInfo
            {
                Id = Key(s.Id),
                Name = s.Name,
                Status = s.Status?.ToString(),
                Tags = (s.Tags ?? Array.Empty<string>()).ToList(),
                ProviderIds = new Dictionary<string, string>(s.ProviderIds ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase),
            };

            if (byShow.TryGetValue(s.Id, out var eps))
            {
                foreach (var g in eps.GroupBy(e => e.SeasonId))
                {
                    seasons.TryGetValue(g.Key, out var season);
                    var number = g.First().ParentIndexNumber ?? season?.IndexNumber;
                    show.Seasons.Add(new SeasonInfo
                    {
                        Id = g.Key != Guid.Empty ? Key(g.Key) : $"{show.Id}:{number}",
                        Number = number,
                        Name = season?.Name ?? (number.HasValue ? $"Season {number}" : "Unknown season"),
                        Tags = (season?.Tags ?? Array.Empty<string>()).ToList(),
                        Episodes = g.Select(e => new EpisodeInfo { Id = Key(e.Id), Season = e.ParentIndexNumber ?? number, Number = e.IndexNumber })
                            .OrderBy(e => e.Number ?? int.MaxValue).ToList(),
                    });
                }
            }

            lib.Shows.Add(show);
        }

        foreach (var m in movies)
        {
            lib.Movies.Add(new MovieInfo
            {
                Id = Key(m.Id),
                Name = m.Name,
                Year = m.ProductionYear,
                Tags = (m.Tags ?? Array.Empty<string>()).ToList(),
                ProviderIds = new Dictionary<string, string>(m.ProviderIds ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase),
            });
        }

        // Played / partly played state, per user.
        var items = episodes.Cast<BaseItem>().Concat(movies).ToList();
        foreach (var u in users)
        {
            ct.ThrowIfCancellationRequested();
            var uid = Key(u.Id);
            foreach (var it in items)
            {
                var d = _userData.GetUserData(u, it);
                if (d is null || (!d.Played && d.PlaybackPositionTicks <= 0))
                {
                    continue;
                }

                var id = Key(it.Id);
                if (!lib.UserData.TryGetValue(id, out var byUser))
                {
                    lib.UserData[id] = byUser = new Dictionary<string, PlayState>(StringComparer.OrdinalIgnoreCase);
                }

                byUser[uid] = new PlayState(d.Played, d.PlaybackPositionTicks, d.LastPlayedDate);
            }
        }

        // Favourites (series, seasons, movies), per user.
        var favouriteCandidates = series.Cast<BaseItem>().Concat(seasons.Values).Concat(movies).ToList();
        foreach (var u in users)
        {
            ct.ThrowIfCancellationRequested();
            var uid = Key(u.Id);
            foreach (var it in favouriteCandidates)
            {
                if (_userData.GetUserData(u, it)?.IsFavorite == true)
                {
                    lib.AddFavorite(Key(it.Id), uid);
                }
            }
        }

        return Task.FromResult(lib);
    }

    public Task RefreshAsync(CancellationToken ct)
    {
        _library.QueueLibraryScan();
        return Task.CompletedTask;
    }
}
