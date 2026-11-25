using System;
using System.Collections.Generic;
using Jellyfin.Plugin.WatchTracker.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.WatchTracker;

/// <summary>Watch Tracker plugin entry point.</summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public const string PluginGuid = "94d2b53c-7f01-48dd-8726-c645ebe5ff9b";

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "Watch Tracker";

    public override Guid Id => Guid.Parse(PluginGuid);

    public override string Description =>
        "Tracks who has started and finished each show and season, flags abandoned viewing, and removes seasons/shows once everyone who started them has finished.";

    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = "WatchTracker",
                DisplayName = "Watch Tracker",
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html",
                EnableInMainMenu = true,
                MenuIcon = "playlist_add_check"
            }
        };
    }
}
