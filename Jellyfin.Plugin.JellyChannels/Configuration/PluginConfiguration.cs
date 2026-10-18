using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellyChannels.Configuration;

/// <summary>
/// Configuración global del plugin. Los canales se guardan aparte, en channels.json.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether epg.xml, channels.m3u y el redirect de stream se sirven sin autenticación.
    /// Si está desactivado, hay que añadir <c>?ApiKey=...</c> a las URLs.
    /// </summary>
    public bool AllowAnonymousGuide { get; set; }

    /// <summary>
    /// Gets or sets los minutos que se cachea la lista de items resuelta de cada canal.
    /// </summary>
    public int LibraryCacheMinutes { get; set; } = 10;

    /// <summary>
    /// Gets or sets los minutos que se cachea el EPG generado.
    /// </summary>
    public int EpgCacheMinutes { get; set; } = 5;
}
