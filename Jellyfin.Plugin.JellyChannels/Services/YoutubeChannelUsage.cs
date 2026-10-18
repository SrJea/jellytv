using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.JellyChannels.Models;

namespace Jellyfin.Plugin.JellyChannels.Services;

/// <summary>
/// Qué vídeos de YouTube usan los canales y cuáles tocan emitir en breve.
/// </summary>
public class YoutubeChannelUsage
{
    private readonly ChannelStore _store;
    private readonly ScheduleService _scheduleService;

    /// <summary>
    /// Initializes a new instance of the <see cref="YoutubeChannelUsage"/> class.
    /// </summary>
    /// <param name="store">Almacén de canales.</param>
    /// <param name="scheduleService">Servicio de parrilla.</param>
    public YoutubeChannelUsage(ChannelStore store, ScheduleService scheduleService)
    {
        _store = store;
        _scheduleService = scheduleService;
    }

    /// <summary>
    /// Devuelve todos los vídeos que usa alguna fuente de YouTube, con su política de retención.
    /// </summary>
    /// <returns>Una referencia por fuente.</returns>
    public IReadOnlyList<YoutubeReference> GetReferences()
    {
        var references = new List<YoutubeReference>();
        foreach (var channel in _store.GetAll())
        {
            foreach (var source in channel.Sources)
            {
                if (source.Type == SourceType.Youtube && YoutubeId.TryParse(source.Url, out var id))
                {
                    references.Add(new YoutubeReference(id, YoutubeRetention.TryParse(source.Retention, out var retention) ? retention : YoutubeRetention.Default));
                }
            }
        }

        return references;
    }

    /// <summary>
    /// Devuelve los huecos de la parrilla que emiten un vídeo de YouTube y caen entre ahora y ahora más el margen.
    /// Incluye el hueco que se está emitiendo.
    /// </summary>
    /// <param name="now">Instante actual.</param>
    /// <param name="margin">Antelación con la que se descarga.</param>
    /// <returns>Los huecos.</returns>
    public IReadOnlyList<YoutubeNeed> GetNeeds(DateTimeOffset now, TimeSpan margin)
    {
        var needs = new List<YoutubeNeed>();
        foreach (var channel in _store.GetAll())
        {
            if (!channel.Sources.Any(s => s.Type == SourceType.Youtube))
            {
                continue;
            }

            foreach (var entry in _scheduleService.GetSchedule(channel).GetEntries(now, now + margin))
            {
                if (entry.Item.YoutubeId is { } id)
                {
                    needs.Add(new YoutubeNeed(id, entry.Start, entry.End));
                }
            }
        }

        return needs;
    }
}
