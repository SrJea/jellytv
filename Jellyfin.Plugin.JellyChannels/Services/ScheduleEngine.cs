using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.JellyChannels.Models;

namespace Jellyfin.Plugin.JellyChannels.Services;

/// <summary>
/// Calcula la parrilla de un canal sin guardar estado: el item actual sale de
/// <c>(ahora - inicio) mod duración_del_ciclo</c>.
/// </summary>
public sealed class ScheduleEngine
{
    private readonly ScheduleItem[] _items;
    private readonly PlaybackMode _mode;
    private readonly long _seed;
    private readonly DateTimeOffset _startDate;
    private readonly CycleOrder _fixedOrder;
    private CycleOrder? _lastShuffled;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduleEngine"/> class.
    /// </summary>
    /// <param name="mode">Modo de ordenación.</param>
    /// <param name="seed">Semilla de barajado.</param>
    /// <param name="startDate">Inicio de la parrilla.</param>
    /// <param name="items">Items en el orden de las fuentes. Los que no tienen duración se descartan.</param>
    public ScheduleEngine(PlaybackMode mode, long seed, DateTimeOffset startDate, IEnumerable<ScheduleItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        _mode = mode;
        _seed = seed;
        _startDate = startDate;
        _items = items.Where(i => i.DurationTicks > 0).ToArray();
        CycleTicks = _items.Sum(i => i.DurationTicks);

        _fixedOrder = mode == PlaybackMode.SeededShuffle
            ? new CycleOrder(-1, StableRandom.Shuffle(_items, _seed))
            : new CycleOrder(-1, _items);
    }

    /// <summary>
    /// Gets a value indicating whether el canal no tiene nada que emitir.
    /// </summary>
    public bool IsEmpty => _items.Length == 0;

    /// <summary>
    /// Gets el número de items del ciclo.
    /// </summary>
    public int ItemCount => _items.Length;

    /// <summary>
    /// Gets la duración total de una vuelta del ciclo en ticks.
    /// </summary>
    public long CycleTicks { get; }

    /// <summary>
    /// Crea el motor para un canal.
    /// </summary>
    /// <param name="channel">El canal.</param>
    /// <param name="items">Items resueltos del canal.</param>
    /// <returns>El motor.</returns>
    public static ScheduleEngine ForChannel(Channel channel, IEnumerable<ScheduleItem> items)
    {
        ArgumentNullException.ThrowIfNull(channel);
        return new ScheduleEngine(
            channel.Mode,
            channel.Seed ?? StableRandom.DefaultSeed(channel.Id),
            channel.StartDate ?? DateTimeOffset.UnixEpoch,
            items);
    }

    /// <summary>
    /// Devuelve el hueco que se emite en un instante.
    /// </summary>
    /// <param name="time">Instante.</param>
    /// <returns>El hueco, o null si el canal está vacío.</returns>
    public ScheduleEntry? GetEntryAt(DateTimeOffset time)
    {
        if (IsEmpty)
        {
            return null;
        }

        long elapsed = (time - _startDate).Ticks;
        long cycle = FloorDiv(elapsed, CycleTicks);
        long position = elapsed - (cycle * CycleTicks);

        var order = GetOrder(cycle);
        int index = Array.BinarySearch(order.Offsets, position);
        if (index < 0)
        {
            index = ~index - 1;
        }

        return CreateEntry(cycle, index, order);
    }

    /// <summary>
    /// Devuelve el hueco siguiente a otro.
    /// </summary>
    /// <param name="entry">Hueco actual.</param>
    /// <returns>El siguiente hueco.</returns>
    public ScheduleEntry GetNext(ScheduleEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (IsEmpty)
        {
            throw new InvalidOperationException("El canal no tiene items.");
        }

        return entry.Index + 1 < _items.Length
            ? CreateEntry(entry.Cycle, entry.Index + 1, GetOrder(entry.Cycle))
            : CreateEntry(entry.Cycle + 1, 0, GetOrder(entry.Cycle + 1));
    }

    /// <summary>
    /// Devuelve los huecos que se solapan con un intervalo.
    /// </summary>
    /// <param name="from">Inicio del intervalo.</param>
    /// <param name="to">Fin del intervalo.</param>
    /// <returns>Los huecos en orden.</returns>
    public IEnumerable<ScheduleEntry> GetEntries(DateTimeOffset from, DateTimeOffset to)
    {
        var entry = GetEntryAt(from);
        while (entry is not null && entry.Start < to)
        {
            yield return entry;
            entry = GetNext(entry);
        }
    }

    private static long FloorDiv(long a, long b)
    {
        long q = a / b;
        return (a % b != 0 && ((a < 0) ^ (b < 0))) ? q - 1 : q;
    }

    private ScheduleEntry CreateEntry(long cycle, int index, CycleOrder order)
    {
        var item = order.Items[index];
        var start = _startDate + TimeSpan.FromTicks((cycle * CycleTicks) + order.Offsets[index]);
        return new ScheduleEntry(item, start, start + TimeSpan.FromTicks(item.DurationTicks), cycle, index);
    }

    private CycleOrder GetOrder(long cycle)
    {
        if (_mode != PlaybackMode.Shuffle)
        {
            return _fixedOrder;
        }

        var last = _lastShuffled;
        if (last is not null && last.Cycle == cycle)
        {
            return last;
        }

        var order = new CycleOrder(cycle, StableRandom.Shuffle(_items, StableRandom.Mix(_seed, cycle)));
        _lastShuffled = order;
        return order;
    }

    private sealed class CycleOrder
    {
        public CycleOrder(long cycle, IReadOnlyList<ScheduleItem> items)
        {
            Cycle = cycle;
            Items = items;
            Offsets = new long[items.Count];
            long offset = 0;
            for (int i = 0; i < items.Count; i++)
            {
                Offsets[i] = offset;
                offset += items[i].DurationTicks;
            }
        }

        public long Cycle { get; }

        public IReadOnlyList<ScheduleItem> Items { get; }

        public long[] Offsets { get; }
    }
}
