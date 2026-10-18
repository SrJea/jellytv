namespace Jellyfin.Plugin.JellyChannels.Models;

/// <summary>
/// Marca de agua superpuesta en el cliente.
/// </summary>
public class Watermark
{
    /// <summary>
    /// Gets or sets el fichero de imagen (URL o nombre dentro de assets).
    /// </summary>
    public string? File { get; set; }

    /// <summary>
    /// Gets or sets la posición: top-left, top-right, bottom-left o bottom-right.
    /// </summary>
    public string Position { get; set; } = "bottom-right";

    /// <summary>
    /// Gets or sets la opacidad entre 0 y 1.
    /// </summary>
    public double Opacity { get; set; } = 0.6;

    /// <summary>
    /// Gets or sets el tamaño en porcentaje del ancho del vídeo.
    /// </summary>
    public int Size { get; set; } = 12;
}
