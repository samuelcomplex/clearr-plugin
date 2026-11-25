using Jellyfin.Plugin.WatchTracker.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.WatchTracker;

/// <summary>Registers the plugin's services with Jellyfin's DI container.</summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<KeepStore>();
        serviceCollection.AddSingleton<WatchAnalyzer>();
        serviceCollection.AddSingleton<HistoryStore>();
        serviceCollection.AddSingleton<SonarrClient>();
        serviceCollection.AddSingleton<FlagTagger>();
        serviceCollection.AddSingleton<CleanupService>();
    }
}
