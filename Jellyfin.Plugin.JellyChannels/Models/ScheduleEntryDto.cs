using System;

namespace Jellyfin.Plugin.JellyChannels.Models;

/// <summary>
/// Hueco de parrilla tal como lo devuelve la API.
/// </summary>
/// <param name="ItemId">Id del item de Jellyfin.</param>
/// <param name="Kind">Tipo de item.</param>
/// <param name="Title">Título.</param>
/// <param name="EpisodeTitle">Título del episodio.</param>
/// <param name="Season">Temporada.</param>
/// <param name="Episode">Episodio.</param>
/// <param name="Start">Inicio.</param>
/// <param name="End">Fin.</param>
/// <param name="DurationTicks">Duración en ticks.</param>
/// <param name="YoutubeId">Id de YouTube si el item es un vídeo de YouTube.</param>
/// <param name="Available">False si el item no se puede emitir ahora (vídeo de YouTube aún sin descargar).</param>
/// <param name="StreamUrl">Para vídeos de YouTube descargados, URL del fichero (admite Range). Los items de la biblioteca se reproducen con la API de Jellyfin.</param>
public sealed record ScheduleEntryDto(
    Guid ItemId,
    string Kind,
    string Title,
    string? EpisodeTitle,
    int? Season,
    int? Episode,
    DateTimeOffset Start,
    DateTimeOffset End,
    long DurationTicks,
    string? YoutubeId = null,
    bool Available = true,
    string? StreamUrl = null)
{
    /// <summary>
    /// Crea el DTO desde un hueco.
    /// </summary>
    /// <param name="entry">Hueco.</param>
    /// <param name="available">False si el item no se puede emitir ahora.</param>
    /// <param name="streamUrl">URL del fichero, si el plugin lo sirve.</param>
    /// <returns>DTO.</returns>
    public static ScheduleEntryDto From(ScheduleEntry entry, bool available = true, string? streamUrl = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var item = entry.Item;
        return new ScheduleEntryDto(item.ItemId, item.Kind, item.Title, item.EpisodeTitle, item.Season, item.Episode, entry.Start, entry.End, item.DurationTicks, item.YoutubeId, available, streamUrl);
    }
}
