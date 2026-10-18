using System;

namespace Jellyfin.Plugin.JellyChannels.Models;

/// <summary>
/// Respuesta de <c>/JellyChannels/channels/{id}/now</c>.
/// </summary>
/// <param name="ChannelId">Id del canal.</param>
/// <param name="ServerTime">Hora del servidor usada para el cálculo.</param>
/// <param name="Current">Item en emisión.</param>
/// <param name="StartTimeTicks">Offset dentro del item actual, para pasarlo como <c>StartTimeTicks</c> al reproductor.</param>
/// <param name="Next">Item siguiente.</param>
/// <param name="ItemCount">Cuántos items resuelven las fuentes del canal (una vuelta de la parrilla).</param>
/// <param name="CycleTicks">Duración de una vuelta de la parrilla en ticks.</param>
/// <param name="Skipped">True si el item que tocaba (un vídeo de YouTube sin descargar) se ha saltado y <paramref name="Current"/> es el siguiente item reproducible, que se empieza desde el principio.</param>
public sealed record NowPlayingDto(string ChannelId, DateTimeOffset ServerTime, ScheduleEntryDto Current, long StartTimeTicks, ScheduleEntryDto Next, int ItemCount = 0, long CycleTicks = 0, bool Skipped = false);
