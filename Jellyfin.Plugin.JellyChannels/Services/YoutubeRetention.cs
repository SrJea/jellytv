using System;
using System.Globalization;

namespace Jellyfin.Plugin.JellyChannels.Services;

/// <summary>
/// Cuándo se borra un vídeo de YouTube de la caché.
/// </summary>
/// <param name="Kind">Tipo de política.</param>
/// <param name="Days">Días, solo para <see cref="YoutubeRetentionKind.KeepDays"/>.</param>
public sealed record YoutubeRetention(YoutubeRetentionKind Kind, int Days = 0)
{
    /// <summary>
    /// Gets la política por defecto: borrar al terminar la emisión.
    /// </summary>
    public static YoutubeRetention Default { get; } = new(YoutubeRetentionKind.DeleteAfterAiring);

    /// <summary>
    /// Lee una política: <c>delete-after-airing</c>, <c>keep-days:N</c> o <c>keep</c>. Vacío es la política por defecto.
    /// </summary>
    /// <param name="value">Texto de la fuente.</param>
    /// <param name="retention">La política.</param>
    /// <returns>False si el texto no es válido.</returns>
    public static bool TryParse(string? value, out YoutubeRetention retention)
    {
        retention = Default;
        var text = value?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(text) || text == "delete-after-airing")
        {
            return true;
        }

        if (text == "keep")
        {
            retention = new YoutubeRetention(YoutubeRetentionKind.Keep);
            return true;
        }

        const string prefix = "keep-days:";
        if (text.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(text[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int days)
            && days is >= 1 and <= 3650)
        {
            retention = new YoutubeRetention(YoutubeRetentionKind.KeepDays, days);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Elige la política más conservadora cuando varios canales usan el mismo vídeo.
    /// </summary>
    /// <param name="a">Una política.</param>
    /// <param name="b">Otra.</param>
    /// <returns>Keep gana a KeepDays (el de más días) y este a DeleteAfterAiring.</returns>
    public static YoutubeRetention MostConservative(YoutubeRetention a, YoutubeRetention b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (a.Kind == YoutubeRetentionKind.Keep || b.Kind == YoutubeRetentionKind.Keep)
        {
            return new YoutubeRetention(YoutubeRetentionKind.Keep);
        }

        if (a.Kind == YoutubeRetentionKind.KeepDays || b.Kind == YoutubeRetentionKind.KeepDays)
        {
            return new YoutubeRetention(YoutubeRetentionKind.KeepDays, Math.Max(a.Days, b.Days));
        }

        return Default;
    }

    /// <summary>
    /// Escribe la política como se guarda en el estado.
    /// </summary>
    /// <returns>El texto.</returns>
    public string ToText() => Kind switch
    {
        YoutubeRetentionKind.Keep => "keep",
        YoutubeRetentionKind.KeepDays => string.Create(CultureInfo.InvariantCulture, $"keep-days:{Days}"),
        _ => "delete-after-airing"
    };
}
