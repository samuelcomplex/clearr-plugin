using Jellyfin.Plugin.Clearr.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Clearr;

/// <summary>Clearr for Jellyfin (formerly Watch Tracker — same id, so it installs as an update).</summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public const string PluginGuid = "94d2b53c-7f01-48dd-8726-c645ebe5ff9b";

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "Clearr";

    public override Guid Id => Guid.Parse(PluginGuid);

    public override string Description =>
        "Clears shows, seasons and movies once everyone who started them has finished — through Sonarr/Radarr, or Jellyfin if you allow it.";

    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = "Clearr",
                DisplayName = "Clearr",
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html",
                EnableInMainMenu = true,
                MenuIcon = "cleaning_services",
            },
        };
    }
}
