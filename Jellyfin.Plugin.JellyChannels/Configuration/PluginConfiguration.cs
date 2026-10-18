using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellyChannels.Configuration;

/// <summary>
/// Configuración global del plugin. Los canales se guardan aparte, en channels.json.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether epg.xml, channels.m3u y el redirect de stream se sirven sin autenticación.
    /// Si está desactivado, hay que añadir <c>?ApiKey=...</c> a las URLs.
    /// </summary>
    public bool AllowAnonymousGuide { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether se ofrece el stream continuo <c>live.ts</c>, que remuxa
    /// (<c>-c copy</c>, sin transcodificar) los ficheros con el ffmpeg de Jellyfin. Desactivado por defecto.
    /// </summary>
    public bool EnableRemux { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether el remux recodifica a H.264 / AAC las pistas que no lo sean.
    /// Si está desactivado, copia siempre y los ficheros de un canal deben compartir códecs.
    /// </summary>
    public bool RemuxAllowTranscode { get; set; } = true;

    /// <summary>
    /// Gets or sets cuántos streams <c>live.ts</c> pueden estar abiertos a la vez; cada uno es un proceso ffmpeg.
    /// </summary>
    public int MaxRemuxStreams { get; set; } = 2;

    /// <summary>
    /// Gets or sets a value indicating whether el plugin registra solo su tuner M3U y su guía XMLTV en Live TV de
    /// Jellyfin y los mantiene al día, en vez de tener que añadirlos a mano.
    /// </summary>
    public bool AutoRegisterLiveTv { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether se admiten fuentes de YouTube (descarga bajo demanda con yt-dlp).
    /// Desactivado por defecto: descargar de YouTube puede incumplir sus condiciones de servicio.
    /// </summary>
    public bool EnableYoutube { get; set; }

    /// <summary>
    /// Gets or sets la ruta del binario de yt-dlp, o su nombre si está en el PATH.
    /// </summary>
    public string YtDlpPath { get; set; } = "yt-dlp";

    /// <summary>
    /// Gets or sets cuántos minutos antes de su emisión se descarga cada vídeo.
    /// </summary>
    public int YoutubePrefetchMinutes { get; set; } = 30;

    /// <summary>
    /// Gets or sets el tamaño máximo de la caché de YouTube en MB; 0 no pone límite.
    /// </summary>
    public int YoutubeMaxCacheMb { get; set; } = 5120;

    /// <summary>
    /// Gets or sets el selector de formato de yt-dlp. El de por defecto pide un solo archivo MP4, que no necesita ffmpeg para unir pistas.
    /// </summary>
    public string YoutubeFormat { get; set; } = "b[ext=mp4]/b";

    /// <summary>
    /// Gets or sets los minutos que se cachea la lista de items resuelta de cada canal.
    /// </summary>
    public int LibraryCacheMinutes { get; set; } = 10;

    /// <summary>
    /// Gets or sets los minutos que se cachea el EPG generado.
    /// </summary>
    public int EpgCacheMinutes { get; set; } = 5;
}
