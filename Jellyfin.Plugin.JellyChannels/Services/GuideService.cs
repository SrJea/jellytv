using System;
using System.Collections.Concurrent;
using System.Linq;

namespace Jellyfin.Plugin.JellyChannels.Services;

/// <summary>
/// Sirve el EPG y el M3U, con el EPG cacheado unos minutos.
/// </summary>
public class GuideService
{
    private const int MaxCachedVariants = 16;

    private readonly ChannelStore _store;
    private readonly ScheduleService _scheduleService;
    private readonly ConcurrentDictionary<GuideUrls, (string Xml, DateTimeOffset BuiltAt)> _epgCache = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="GuideService"/> class.
    /// </summary>
    /// <param name="store">Almacén de canales.</param>
    /// <param name="scheduleService">Servicio de parrilla.</param>
    public GuideService(ChannelStore store, ScheduleService scheduleService)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _scheduleService = scheduleService;
        _store.Changed += (_, _) => _epgCache.Clear();
        _scheduleService.Invalidated += (_, _) => _epgCache.Clear();
    }

    internal int CacheCount => _epgCache.Count;

    /// <summary>
    /// Devuelve el XMLTV de todos los canales.
    /// </summary>
    /// <param name="urls">Constructor de URLs.</param>
    /// <returns>Documento XMLTV.</returns>
    public string GetEpg(GuideUrls urls)
    {
        var now = _scheduleService.Now;
        var ttl = TimeSpan.FromMinutes(Math.Max(0, Plugin.Instance?.Configuration.EpgCacheMinutes ?? 5));
        if (_epgCache.TryGetValue(urls, out var cached) && now - cached.BuiltAt < ttl)
        {
            return cached.Xml;
        }

        var channels = _store.GetAll().Select(c => (c, _scheduleService.GetSchedule(c))).ToList();
        var xml = EpgGenerator.Generate(channels, urls, now);

        // La clave incluye el host y la clave de API de la petición; sin tope, un cliente anónimo
        // podría llenar la memoria variando la cabecera Host.
        if (_epgCache.Count >= MaxCachedVariants)
        {
            _epgCache.Clear();
        }

        _epgCache[urls] = (xml, now);
        return xml;
    }

    /// <summary>
    /// Devuelve el M3U de todos los canales.
    /// </summary>
    /// <param name="urls">Constructor de URLs.</param>
    /// <returns>Contenido M3U.</returns>
    public string GetM3u(GuideUrls urls) => M3uGenerator.Generate(_store.GetAll(), urls);
}
