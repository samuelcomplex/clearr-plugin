// Reference stubs for MediaBrowser.Controller 10.11 (signatures copied from v10.11.11).
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace MediaBrowser.Controller
{
    public interface IServerApplicationHost { }
}

namespace MediaBrowser.Controller.Plugins
{
    public interface IPluginServiceRegistrator
    {
        void RegisterServices(Microsoft.Extensions.DependencyInjection.IServiceCollection serviceCollection, IServerApplicationHost applicationHost);
    }
}

namespace MediaBrowser.Controller.Library
{
    public interface ILibraryManager
    {
        BaseItem? GetItemById(Guid id);
        IReadOnlyList<BaseItem> GetItemList(InternalItemsQuery query);
        void DeleteItem(BaseItem item, DeleteOptions options, bool notifyParentItem);
    }

    public interface IUserManager
    {
        IEnumerable<User> GetUsers();
        User? GetUserById(Guid id);
    }

    public interface IUserDataManager
    {
        UserItemData? GetUserData(User user, BaseItem item);
    }
}

namespace MediaBrowser.Controller.Entities
{
    public abstract class BaseItem
    {
        public bool IsVirtualItem { get; set; }
        public Guid Id { get; set; }
        public virtual string Name { get; set; } = string.Empty;
        public virtual string Path { get; set; } = string.Empty;
        public DateTime DateCreated { get; set; }
        public string[] Tags { get; set; } = Array.Empty<string>();
        public int? IndexNumber { get; set; }
        public int? ParentIndexNumber { get; set; }
        public Dictionary<string, string> ProviderIds { get; set; } = new();
        public virtual Task UpdateToRepositoryAsync(ItemUpdateType updateReason, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public class Folder : BaseItem { }

    public class Video : BaseItem { }

    public class UserItemData
    {
        public long PlaybackPositionTicks { get; set; }
        public int PlayCount { get; set; }
        public DateTime? LastPlayedDate { get; set; }
        public bool Played { get; set; }
    }

    public class InternalItemsQuery
    {
        public bool Recursive { get; set; }
        public BaseItemKind[] IncludeItemTypes { get; set; } = Array.Empty<BaseItemKind>();
        public bool? IsVirtualItem { get; set; }
        public Guid ParentId { get; set; }
        public Guid[] AncestorIds { get; set; } = Array.Empty<Guid>();
    }
}

namespace MediaBrowser.Controller.Entities.TV
{
    public class Episode : Video
    {
        public Guid SeasonId { get; set; }
        public Guid SeriesId { get; set; }
    }

    public class Season : Folder
    {
        public Guid SeriesId { get; set; }
    }

    public class Series : Folder
    {
        public SeriesStatus? Status { get; set; }
    }
}
