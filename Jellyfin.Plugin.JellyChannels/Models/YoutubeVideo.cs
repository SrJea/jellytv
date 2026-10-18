using System;

namespace Jellyfin.Plugin.JellyChannels.Models;

/// <summary>
/// Un vídeo de YouTube conocido: sus metadatos y el estado de su copia en la caché.
/// </summary>
public class YoutubeVideo
{
    /// <summary>
    /// Gets or sets el id de YouTube.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets el título, o null si aún no se han leído los metadatos.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets la duración en segundos.
    /// </summary>
    public double DurationSeconds { get; set; }

    /// <summary>
    /// Gets or sets la URL de la miniatura.
    /// </summary>
    public string? Thumbnail { get; set; }

    /// <summary>
    /// Gets or sets la descripción.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets el canal que lo subió.
    /// </summary>
    public string? Uploader { get; set; }

    /// <summary>
    /// Gets or sets el códec de vídeo que anunció yt-dlp para el formato elegido.
    /// </summary>
    public string? VideoCodec { get; set; }

    /// <summary>
    /// Gets or sets el códec de audio que anunció yt-dlp para el formato elegido.
    /// </summary>
    public string? AudioCodec { get; set; }

    /// <summary>
    /// Gets or sets la extensión del formato elegido.
    /// </summary>
    public string? Ext { get; set; }

    /// <summary>
    /// Gets or sets cuándo se leyeron los metadatos.
    /// </summary>
    public DateTimeOffset? MetadataAt { get; set; }

    /// <summary>
    /// Gets or sets el nombre del fichero dentro de la carpeta de caché, o null si no está descargado.
    /// </summary>
    public string? File { get; set; }

    /// <summary>
    /// Gets or sets el tamaño del fichero en bytes.
    /// </summary>
    public long SizeBytes { get; set; }

    /// <summary>
    /// Gets or sets cuándo se descargó.
    /// </summary>
    public DateTimeOffset? DownloadedAt { get; set; }

    /// <summary>
    /// Gets or sets la última vez que hizo falta (su hueco estaba dentro de la ventana de descarga).
    /// </summary>
    public DateTimeOffset? LastNeededAt { get; set; }

    /// <summary>
    /// Gets or sets la política de retención más conservadora de las fuentes que lo usan.
    /// </summary>
    public string Retention { get; set; } = "delete-after-airing";

    /// <summary>
    /// Gets or sets los fallos seguidos (metadatos o descarga).
    /// </summary>
    public int Failures { get; set; }

    /// <summary>
    /// Gets or sets cuándo se puede volver a intentar tras un fallo.
    /// </summary>
    public DateTimeOffset? NextAttemptAt { get; set; }

    /// <summary>
    /// Gets or sets el último error.
    /// </summary>
    public string? LastError { get; set; }
}
