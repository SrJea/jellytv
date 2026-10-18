using System;
using System.Collections.Concurrent;
using Jellyfin.Plugin.JellyChannels.Models;

namespace Jellyfin.Plugin.JellyChannels.Services;

/// <summary>
/// Da el <see cref="ScheduleEngine"/> de cada canal, cacheando la resolución de la biblioteca
/// unos minutos para no consultar la base de datos en cada petición.
/// </summary>
public class ScheduleService
{
    private readonly ChannelStore _store;
    private readonly ILibraryResolver _resolver;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, CachedSchedule> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduleService"/> class.
    /// </summary>
    /// <param name="store">Almacén de canales.</param>
    /// <param name="resolver">Resolver de biblioteca.</param>
    public ScheduleService(ChannelStore store, ILibraryResolver resolver)
        : this(store, resolver, TimeProvider.System)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduleService"/> class.
    /// </summary>
    /// <param name="store">Almacén de canales.</param>
    /// <param name="resolver">Resolver de biblioteca.</param>
    /// <param name="timeProvider">Reloj.</param>
    public ScheduleService(ChannelStore store, ILibraryResolver resolver, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _resolver = resolver;
        _timeProvider = timeProvider;
        _store.Changed += (sender, e) => _cache.TryRemove(e.ChannelId, out var _);
    }

    /// <summary>
    /// Se lanza al vaciar toda la caché con <see cref="Invalidate"/>.
    /// </summary>
    public event EventHandler? Invalidated;

    /// <summary>
    /// Gets la hora actual según el reloj del servicio.
    /// </summary>
    public DateTimeOffset Now => _timeProvider.GetUtcNow();

    /// <summary>
    /// Devuelve el motor de parrilla de un canal.
    /// </summary>
    /// <param name="channel">El canal.</param>
    /// <returns>El motor.</returns>
    public ScheduleEngine GetSchedule(Channel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        var ttl = TimeSpan.FromMinutes(Math.Max(0, Plugin.Instance?.Configuration.LibraryCacheMinutes ?? 10));
        var now = Now;
        if (_cache.TryGetValue(channel.Id, out var cached) && now - cached.BuiltAt < ttl)
        {
            return cached.Engine;
        }

        var engine = ScheduleEngine.ForChannel(channel, _resolver.Resolve(channel));
        _cache[channel.Id] = new CachedSchedule(engine, now);
        return engine;
    }

    /// <summary>
    /// Vacía la caché de todos los canales.
    /// </summary>
    public void Invalidate()
    {
        _cache.Clear();
        Invalidated?.Invoke(this, EventArgs.Empty);
    }

    private sealed record CachedSchedule(ScheduleEngine Engine, DateTimeOffset BuiltAt);
}
