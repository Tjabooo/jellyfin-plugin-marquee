using Jellyfin.Plugin.Lilin.Sync;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Lilin;

public class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<LilinClient>();
        serviceCollection.AddSingleton<LibrarySync>();
        serviceCollection.AddHostedService<ProgressReporter>();
    }
}
