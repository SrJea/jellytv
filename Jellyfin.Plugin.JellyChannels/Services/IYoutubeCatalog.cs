using Jellyfin.Plugin.JellyChannels.Models;

namespace Jellyfin.Plugin.JellyChannels.Services;

/// <summary>
/// Los vídeos de YouTube que ya se pueden meter en una parrilla (los que tienen metadatos).
/// </summary>
public interface IYoutubeCatalog
{
    /// <summary>
    /// Devuelve el item de parrilla de un vídeo.
    /// </summary>
    /// <param name="videoId">Id de YouTube.</param>
    /// <returns>El item, o null si YouTube está desactivado o aún no se han leído los metadatos.</returns>
    ScheduleItem? GetScheduleItem(string videoId);
}
