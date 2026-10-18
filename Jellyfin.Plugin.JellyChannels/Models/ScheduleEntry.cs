using System;

namespace Jellyfin.Plugin.JellyChannels.Models;

/// <summary>
/// Hueco de la parrilla: un item con su hora de inicio y fin.
/// </summary>
/// <param name="Item">El item emitido.</param>
/// <param name="Start">Inicio.</param>
/// <param name="End">Fin.</param>
/// <param name="Cycle">Vuelta del ciclo a la que pertenece.</param>
/// <param name="Index">Posición dentro de la vuelta.</param>
public sealed record ScheduleEntry(ScheduleItem Item, DateTimeOffset Start, DateTimeOffset End, long Cycle, int Index)
{
    /// <summary>
    /// Devuelve cuánto lleva emitiéndose el item en el instante dado.
    /// </summary>
    /// <param name="now">Instante de referencia.</param>
    /// <returns>Offset en ticks dentro del item.</returns>
    public long OffsetTicksAt(DateTimeOffset now) => Math.Clamp((now - Start).Ticks, 0, Item.DurationTicks);
}
