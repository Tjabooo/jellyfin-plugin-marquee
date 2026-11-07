using Jellyfin.Plugin.Marquee.Sync;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Marquee;

public class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<MarqueeClient>();
        serviceCollection.AddSingleton<LibrarySync>();
        serviceCollection.AddHostedService<ProgressReporter>();
    }
}
