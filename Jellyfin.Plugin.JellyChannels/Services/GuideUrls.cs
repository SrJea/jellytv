using System;
using System.Globalization;
using Jellyfin.Plugin.JellyChannels.Models;

namespace Jellyfin.Plugin.JellyChannels.Services;

/// <summary>
/// Construye las URLs públicas que aparecen en el M3U y en el XMLTV.
/// </summary>
/// <param name="BaseUrl">URL base del servidor, sin barra final (incluye la BaseUrl de Jellyfin si la hay).</param>
/// <param name="ApiKey">Clave de API a propagar en las URLs del plugin, o null.</param>
public sealed record GuideUrls(string BaseUrl, string? ApiKey)
{
    /// <summary>
    /// Id del canal en XMLTV y en <c>tvg-id</c>.
    /// </summary>
    /// <param name="channel">El canal.</param>
    /// <returns>El id.</returns>
    public static string XmltvId(Channel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        return channel.Id + ".jellychannels";
    }

    /// <summary>
    /// URL del EPG.
    /// </summary>
    /// <returns>La URL.</returns>
    public string Epg() => WithKey($"{BaseUrl}/JellyChannels/epg.xml");

    /// <summary>
    /// URL del stream de un canal.
    /// </summary>
    /// <param name="channel">El canal.</param>
    /// <returns>La URL.</returns>
    public string Stream(Channel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        return WithKey($"{BaseUrl}/JellyChannels/channels/{Uri.EscapeDataString(channel.Id)}/stream");
    }

    /// <summary>
    /// URL del stream continuo remuxado de un canal.
    /// </summary>
    /// <param name="channel">El canal.</param>
    /// <returns>La URL.</returns>
    public string Live(Channel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        return WithKey($"{BaseUrl}/JellyChannels/channels/{Uri.EscapeDataString(channel.Id)}/live.ts");
    }

    /// <summary>
    /// URL del fichero descargado de un vídeo de YouTube, que se sirve con soporte de rangos.
    /// </summary>
    /// <param name="videoId">Id de YouTube.</param>
    /// <returns>La URL.</returns>
    public string YoutubeFile(string videoId)
        => WithKey($"{BaseUrl}/JellyChannels/youtube/{Uri.EscapeDataString(videoId)}/file");

    /// <summary>
    /// URL de un fichero de la carpeta assets, o la propia URL si ya es absoluta.
    /// </summary>
    /// <param name="file">Nombre de fichero o URL.</param>
    /// <returns>La URL, o null.</returns>
    public string? Asset(string? file)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            return null;
        }

        if (Uri.TryCreate(file, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return file;
        }

        return $"{BaseUrl}/JellyChannels/assets/{Uri.EscapeDataString(file)}";
    }

    /// <summary>
    /// URL de la imagen Primary de un item (endpoint de Jellyfin, no requiere autenticación).
    /// </summary>
    /// <param name="itemId">Id del item.</param>
    /// <returns>La URL.</returns>
    public string ItemImage(Guid itemId)
        => string.Create(CultureInfo.InvariantCulture, $"{BaseUrl}/Items/{itemId:N}/Images/Primary");

    private string WithKey(string url)
        => string.IsNullOrEmpty(ApiKey) ? url : $"{url}?ApiKey={Uri.EscapeDataString(ApiKey)}";
}
