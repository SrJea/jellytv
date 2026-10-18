using Jellyfin.Plugin.JellyChannels.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.JellyChannels;

/// <summary>
/// Registra los servicios del plugin en el contenedor de Jellyfin.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<ChannelStore>();
        serviceCollection.AddSingleton<ILibraryResolver, LibraryResolver>();
        serviceCollection.AddSingleton<ScheduleService>();
        serviceCollection.AddSingleton<GuideService>();
        serviceCollection.AddSingleton<IFfmpegRunner, FfmpegProcessRunner>();
        serviceCollection.AddSingleton<RemuxService>();
    }
}
