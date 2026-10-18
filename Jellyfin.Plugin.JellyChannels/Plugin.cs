using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.JellyChannels.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.JellyChannels;

/// <summary>
/// Punto de entrada del plugin JellyChannels.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Identificador del plugin.
    /// </summary>
    public const string PluginGuid = "83abdf93-106c-418c-be99-ed5dad2ef9c6";

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "JellyChannels";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse(PluginGuid);

    /// <inheritdoc />
    public override string Description => "Canales de TV lineales a partir de la biblioteca, con EPG XMLTV y lista M3U.";

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,

                // Sin EnableInMainMenu la página solo se alcanza desde Complementos > JellyChannels. DisplayName es la
                // etiqueta del menú lateral y MenuIcon el nombre de un icono de Material Icons.
                DisplayName = Name,
                EnableInMainMenu = true,
                MenuSection = "server",
                MenuIcon = "live_tv",
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace)
            }
        ];
    }
}
