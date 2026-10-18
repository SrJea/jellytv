using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.JellyChannels.Models;

namespace Jellyfin.Plugin.JellyChannels.Services;

/// <summary>
/// Rechaza al guardar las fuentes que nunca podrían resolverse, en vez de dejar un canal
/// vacío sin explicación.
/// </summary>
internal static class ChannelValidator
{
    /// <summary>
    /// Valida las fuentes de un canal.
    /// </summary>
    /// <param name="channel">El canal.</param>
    /// <returns>Un mensaje por cada problema; vacío si es válido.</returns>
    public static IReadOnlyList<string> Validate(Channel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        var errors = new List<string>();
        for (int i = 0; i < channel.Sources.Count; i++)
        {
            var source = channel.Sources[i];
            string? problem = source.Type switch
            {
                SourceType.Series or SourceType.Movie or SourceType.Collection
                    when !Guid.TryParse(source.Id, out _) => "necesita un 'id' de item de Jellyfin válido",
                SourceType.Tag when string.IsNullOrWhiteSpace(source.Value) => "necesita un 'value' con el tag",
                SourceType.Youtube when !YoutubeId.TryParse(source.Url, out _)
                    => "necesita la 'url' de un vídeo de YouTube (watch?v=, youtu.be, shorts); las listas de reproducción aún no están soportadas",
                _ => null
            };

            if (problem is null && source.Type == SourceType.Youtube && !YoutubeRetention.TryParse(source.Retention, out _))
            {
                problem = "'retention' debe ser delete-after-airing, keep o keep-days:N (N entre 1 y 3650)";
            }

            if (problem is not null)
            {
                errors.Add(string.Create(CultureInfo.InvariantCulture, $"sources[{i}] ({source.Type.ToString().ToLowerInvariant()}): {problem}."));
            }
        }

        return errors;
    }
}
