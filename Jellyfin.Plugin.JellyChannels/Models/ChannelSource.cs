namespace Jellyfin.Plugin.JellyChannels.Models;

/// <summary>
/// Fuente de contenido de un canal.
/// </summary>
public class ChannelSource
{
    /// <summary>
    /// Gets or sets el tipo de fuente.
    /// </summary>
    public SourceType Type { get; set; }

    /// <summary>
    /// Gets or sets el id del item de Jellyfin (series, movie, collection).
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets el valor (tag).
    /// </summary>
    public string? Value { get; set; }

    /// <summary>
    /// Gets or sets la URL (youtube, v0.6).
    /// </summary>
#pragma warning disable CA1056 // Se guarda tal cual la pega el usuario.
    public string? Url { get; set; }
#pragma warning restore CA1056

    /// <summary>
    /// Gets or sets la política de retención (youtube, v0.6).
    /// </summary>
    public string? Retention { get; set; }
}
