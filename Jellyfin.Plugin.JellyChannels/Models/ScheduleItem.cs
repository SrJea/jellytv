using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyChannels.Models;

/// <summary>
/// Item emitible con su duración y los metadatos que necesita el EPG.
/// </summary>
public sealed record ScheduleItem
{
    /// <summary>
    /// Gets el id del item de Jellyfin.
    /// </summary>
    public required Guid ItemId { get; init; }

    /// <summary>
    /// Gets la duración en ticks.
    /// </summary>
    public required long DurationTicks { get; init; }

    /// <summary>
    /// Gets el título principal (nombre de la serie para episodios).
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Gets el título del episodio.
    /// </summary>
    public string? EpisodeTitle { get; init; }

    /// <summary>
    /// Gets la sinopsis.
    /// </summary>
    public string? Overview { get; init; }

    /// <summary>
    /// Gets el número de temporada.
    /// </summary>
    public int? Season { get; init; }

    /// <summary>
    /// Gets el número de episodio.
    /// </summary>
    public int? Episode { get; init; }

    /// <summary>
    /// Gets el año de producción.
    /// </summary>
    public int? Year { get; init; }

    /// <summary>
    /// Gets la clasificación por edades.
    /// </summary>
    public string? OfficialRating { get; init; }

    /// <summary>
    /// Gets los géneros.
    /// </summary>
    public IReadOnlyList<string> Genres { get; init; } = [];

    /// <summary>
    /// Gets el tipo de item de Jellyfin (Episode, Movie...).
    /// </summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>
    /// Gets la ruta del fichero en disco, o null si el item no es un fichero local. La usa el remux.
    /// </summary>
    public string? Path { get; init; }

    /// <summary>
    /// Gets el id del item cuya imagen Primary se usa como póster.
    /// </summary>
    public Guid? ImageItemId { get; init; }
}
