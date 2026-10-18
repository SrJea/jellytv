namespace Jellyfin.Plugin.JellyChannels.Services;

/// <summary>
/// Qué hay que hacer con una entrada de Live TV (tuner o guía).
/// </summary>
public enum LiveTvAction
{
    /// <summary>
    /// Ya está bien.
    /// </summary>
    None,

    /// <summary>
    /// No existe: hay que crearla.
    /// </summary>
    Create,

    /// <summary>
    /// Existe con otra URL: hay que actualizarla.
    /// </summary>
    Update
}
