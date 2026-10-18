using Jellyfin.Plugin.JellyChannels.Models;

namespace Jellyfin.Plugin.JellyChannels.Services;

/// <summary>
/// Localiza el fichero de un item y lee los códecs de sus pistas en el momento de emitirlo.
/// </summary>
public interface IPlaybackSourceResolver
{
    /// <summary>
    /// Resuelve un item.
    /// </summary>
    /// <param name="item">El item de la parrilla.</param>
    /// <returns>El fichero y sus pistas, o null si el item no se puede emitir ahora (no es un fichero local o aún no está descargado).</returns>
    PlaybackSource? Resolve(ScheduleItem item);
}
