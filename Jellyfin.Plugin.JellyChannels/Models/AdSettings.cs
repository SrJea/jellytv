namespace Jellyfin.Plugin.JellyChannels.Models;

/// <summary>
/// Ajustes de anuncios de un canal.
/// </summary>
public class AdSettings
{
    /// <summary>
    /// Gets or sets la carpeta de anuncios.
    /// </summary>
    public string? Folder { get; set; }

    /// <summary>
    /// Gets or sets cada cuántos minutos hay pausa.
    /// </summary>
    public int EveryMinutes { get; set; } = 15;

    /// <summary>
    /// Gets or sets el máximo de anuncios por pausa.
    /// </summary>
    public int MaxPerBreak { get; set; } = 3;
}
