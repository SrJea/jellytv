namespace Jellyfin.Plugin.JellyChannels.Models;

/// <summary>
/// Tipo de fuente de un canal.
/// </summary>
public enum SourceType
{
    /// <summary>
    /// Una serie: todos sus episodios en orden.
    /// </summary>
    Series,

    /// <summary>
    /// Una película o vídeo suelto.
    /// </summary>
    Movie,

    /// <summary>
    /// Una colección (BoxSet).
    /// </summary>
    Collection,

    /// <summary>
    /// Todos los items con un tag.
    /// </summary>
    Tag,

    /// <summary>
    /// Vídeo de YouTube (pendiente, v0.6).
    /// </summary>
    Youtube
}
