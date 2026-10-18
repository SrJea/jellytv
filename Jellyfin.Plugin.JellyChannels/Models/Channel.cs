using System;
using System.Collections.ObjectModel;

namespace Jellyfin.Plugin.JellyChannels.Models;

/// <summary>
/// Canal lineal.
/// </summary>
public class Channel
{
    /// <summary>
    /// Gets or sets el identificador del canal (slug).
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets el nombre visible.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets el número de canal.
    /// </summary>
    public int Number { get; set; }

    /// <summary>
    /// Gets or sets el logo: URL absoluta o nombre de fichero dentro de la carpeta assets del plugin.
    /// </summary>
    public string? Logo { get; set; }

    /// <summary>
    /// Gets or sets la marca de agua (se usa en la ventana propia, v0.2).
    /// </summary>
    public Watermark? Watermark { get; set; }

    /// <summary>
    /// Gets the fuentes del canal.
    /// </summary>
    public Collection<ChannelSource> Sources { get; init; } = [];

    /// <summary>
    /// Gets or sets el modo de ordenación.
    /// </summary>
    public PlaybackMode Mode { get; set; } = PlaybackMode.Sequential;

    /// <summary>
    /// Gets or sets los ajustes de anuncios (v0.3).
    /// </summary>
    public AdSettings? Ads { get; set; }

    /// <summary>
    /// Gets or sets cuántos días de EPG se generan.
    /// </summary>
    public int EpgDays { get; set; } = 3;

    /// <summary>
    /// Gets or sets la semilla de barajado. Si es null se deriva del id.
    /// </summary>
    public long? Seed { get; set; }

    /// <summary>
    /// Gets or sets el instante en que arranca la parrilla.
    /// </summary>
    public DateTimeOffset? StartDate { get; set; }
}
