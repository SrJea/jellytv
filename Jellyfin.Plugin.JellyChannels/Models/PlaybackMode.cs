namespace Jellyfin.Plugin.JellyChannels.Models;

/// <summary>
/// Orden de emisión de los items de un canal.
/// </summary>
public enum PlaybackMode
{
    /// <summary>
    /// En el orden de las fuentes.
    /// </summary>
    Sequential,

    /// <summary>
    /// Barajado distinto en cada vuelta del ciclo.
    /// </summary>
    Shuffle,

    /// <summary>
    /// Barajado fijo con la semilla del canal.
    /// </summary>
    SeededShuffle
}
