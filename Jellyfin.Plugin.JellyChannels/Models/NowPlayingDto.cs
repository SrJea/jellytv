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
public sealed record NowPlayingDto(string ChannelId, DateTimeOffset ServerTime, ScheduleEntryDto Current, long StartTimeTicks, ScheduleEntryDto Next);
