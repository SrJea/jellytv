using System.Collections.Generic;
using Jellyfin.Plugin.JellyChannels.Models;

namespace Jellyfin.Plugin.JellyChannels.Services;

/// <summary>
/// Convierte las fuentes de un canal en items emitibles.
/// </summary>
public interface ILibraryResolver
{
    /// <summary>
    /// Resuelve las fuentes de un canal, sin duplicados y en orden.
    /// </summary>
    /// <param name="channel">El canal.</param>
    /// <returns>Items con duración.</returns>
    IReadOnlyList<ScheduleItem> Resolve(Channel channel);
}
