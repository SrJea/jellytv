using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.JellyChannels.Services;

/// <summary>
/// Identificadores de vídeo de YouTube. Solo se acepta un id de 11 caracteres y la URL que se le pasa a
/// yt-dlp siempre se construye aquí, nunca es la que escribió el usuario: así una fuente no puede hacer
/// que yt-dlp descargue de otro sitio ni colar opciones en su línea de comandos.
/// </summary>
public static partial class YoutubeId
{
    /// <summary>
    /// Tipo de item de la parrilla para los vídeos de YouTube.
    /// </summary>
    public const string Kind = "YouTube";

    /// <summary>
    /// Extrae el id de una URL de vídeo de YouTube o de un id suelto.
    /// </summary>
    /// <param name="urlOrId">URL de <c>watch</c>, <c>youtu.be</c>, <c>shorts</c>, <c>live</c> o <c>embed</c>, o el id.</param>
    /// <param name="id">El id de 11 caracteres.</param>
    /// <returns>False si no es un vídeo (por ejemplo, una lista de reproducción o una URL de otro sitio).</returns>
    public static bool TryParse(string? urlOrId, out string id)
    {
        id = string.Empty;
        var text = urlOrId?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        if (IsValid(text))
        {
            id = text;
            return true;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal))
        {
            host = host[4..];
        }

        string? candidate = null;
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (host == "youtu.be")
        {
            candidate = segments.Length > 0 ? segments[0] : null;
        }
        else if (host is "youtube.com" or "m.youtube.com" or "music.youtube.com" or "youtube-nocookie.com")
        {
            if (segments.Length == 1 && segments[0].Equals("watch", StringComparison.OrdinalIgnoreCase))
            {
                candidate = QueryValue(uri.Query, "v");
            }
            else if (segments.Length == 2 && segments[0].ToLowerInvariant() is "shorts" or "live" or "embed" or "v")
            {
                candidate = segments[1];
            }
        }

        if (candidate is not null && IsValid(candidate))
        {
            id = candidate;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Indica si es un id de vídeo con la forma correcta.
    /// </summary>
    /// <param name="id">Texto.</param>
    /// <returns>True si son 11 caracteres de <c>A-Z a-z 0-9 _ -</c>.</returns>
    public static bool IsValid(string? id) => id is not null && IdPattern().IsMatch(id);

    /// <summary>
    /// URL canónica que se le pasa a yt-dlp.
    /// </summary>
    /// <param name="id">Id válido.</param>
    /// <returns>La URL.</returns>
    public static string CanonicalUrl(string id)
    {
        if (!IsValid(id))
        {
            throw new ArgumentException("Id de YouTube no válido.", nameof(id));
        }

        return "https://www.youtube.com/watch?v=" + id;
    }

    /// <summary>
    /// Guid estable para un vídeo, para ocupar el lugar del id de item de Jellyfin en la parrilla.
    /// </summary>
    /// <param name="id">Id de YouTube.</param>
    /// <returns>El Guid.</returns>
    public static Guid ToGuid(string id)
        => new(SHA256.HashData(Encoding.UTF8.GetBytes("jellychannels:youtube:" + id)).AsSpan(0, 16));

    private static string? QueryValue(string query, string key)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts[0] == key && parts.Length == 2)
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        return null;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{11}$")]
    private static partial Regex IdPattern();
}
