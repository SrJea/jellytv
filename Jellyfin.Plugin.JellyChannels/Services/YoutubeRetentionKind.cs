namespace Jellyfin.Plugin.JellyChannels.Services;

/// <summary>
/// Tipo de política de retención.
/// </summary>
public enum YoutubeRetentionKind
{
    /// <summary>
    /// Se borra al terminar su emisión.
    /// </summary>
    DeleteAfterAiring,

    /// <summary>
    /// Se conserva N días tras su última emisión.
    /// </summary>
    KeepDays,

    /// <summary>
    /// No se borra automáticamente.
    /// </summary>
    Keep
}
