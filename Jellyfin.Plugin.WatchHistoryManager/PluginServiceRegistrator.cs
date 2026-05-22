using Jellyfin.Plugin.WatchHistoryManager.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.WatchHistoryManager;

/// <summary>
/// Registers services for the plugin.
/// </summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddHostedService<PlaybackMonitorService>();
        serviceCollection.AddHostedService<JavaScriptInjectorRegistrationService>();
    }
}
