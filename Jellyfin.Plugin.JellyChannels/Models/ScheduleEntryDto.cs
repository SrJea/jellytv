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
public sealed record ScheduleEntryDto(
    Guid ItemId,
    string Kind,
    string Title,
    string? EpisodeTitle,
    int? Season,
    int? Episode,
    DateTimeOffset Start,
    DateTimeOffset End,
    long DurationTicks)
{
    /// <summary>
    /// Crea el DTO desde un hueco.
    /// </summary>
    /// <param name="entry">Hueco.</param>
    /// <returns>DTO.</returns>
    public static ScheduleEntryDto From(ScheduleEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var item = entry.Item;
        return new ScheduleEntryDto(item.ItemId, item.Kind, item.Title, item.EpisodeTitle, item.Season, item.Episode, entry.Start, entry.End, item.DurationTicks);
    }
}
