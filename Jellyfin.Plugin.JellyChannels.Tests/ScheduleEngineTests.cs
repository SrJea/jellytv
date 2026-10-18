using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.JellyChannels.Models;
using Jellyfin.Plugin.JellyChannels.Services;
using static Jellyfin.Plugin.JellyChannels.Tests.TestItems;

namespace Jellyfin.Plugin.JellyChannels.Tests;

public class ScheduleEngineTests
{
    private static readonly ScheduleItem A = Item("A", 10);
    private static readonly ScheduleItem B = Item("B", 20);
    private static readonly ScheduleItem C = Item("C", 30);
    private static readonly int[] PinnedShuffle = [0, 9, 5, 8, 6, 4, 7, 2, 1, 3];

    private static ScheduleEngine Sequential(params ScheduleItem[] items) => new(PlaybackMode.Sequential, 1, Start, items);

    [Fact]
    public void EmptyChannel_HasNoEntries()
    {
        var engine = Sequential();

        Assert.True(engine.IsEmpty);
        Assert.Null(engine.GetEntryAt(Start));
        Assert.Empty(engine.GetEntries(Start, Start.AddDays(1)));
    }

    [Fact]
    public void ItemsWithoutDuration_AreSkipped()
    {
        var engine = Sequential(A, Item("Sin duración", 0), B);

        Assert.Equal(2, engine.ItemCount);
        Assert.Equal(TimeSpan.FromMinutes(30).Ticks, engine.CycleTicks);
    }

    [Theory]
    [InlineData(0, "A", 0)]
    [InlineData(9.5, "A", 9.5)]
    [InlineData(10, "B", 0)]
    [InlineData(25, "B", 15)]
    [InlineData(30, "C", 0)]
    [InlineData(59, "C", 29)]
    [InlineData(60, "A", 0)]
    [InlineData(75, "B", 5)]
    [InlineData(-5, "C", 25)]
    [InlineData(-60, "A", 0)]
    public void Sequential_CurrentItemIsElapsedModCycle(double minutesFromStart, string expectedTitle, double expectedOffsetMinutes)
    {
        var engine = Sequential(A, B, C);
        var now = Start.AddMinutes(minutesFromStart);

        var entry = engine.GetEntryAt(now)!;

        Assert.Equal(expectedTitle, entry.Item.Title);
        Assert.Equal(TimeSpan.FromMinutes(expectedOffsetMinutes).Ticks, entry.OffsetTicksAt(now));
        Assert.True(entry.Start <= now && now < entry.End);
    }

    [Fact]
    public void GetNext_WrapsToNextCycle()
    {
        var engine = Sequential(A, B, C);
        var last = engine.GetEntryAt(Start.AddMinutes(45))!;

        var next = engine.GetNext(last);

        Assert.Equal("A", next.Item.Title);
        Assert.Equal(Start.AddMinutes(60), next.Start);
        Assert.Equal(last.Cycle + 1, next.Cycle);
    }

    [Theory]
    [InlineData(PlaybackMode.Sequential)]
    [InlineData(PlaybackMode.Shuffle)]
    [InlineData(PlaybackMode.SeededShuffle)]
    public void GetEntries_AreContiguousAndCoverTheRange(PlaybackMode mode)
    {
        var engine = new ScheduleEngine(mode, 7, Start, Many(15));
        var from = Start.AddDays(3).AddMinutes(13);
        var to = from.AddDays(3);

        var entries = engine.GetEntries(from, to).ToList();

        Assert.True(entries[0].Start <= from && from < entries[0].End);
        Assert.True(entries[^1].Start < to && to <= entries[^1].End);
        for (int i = 1; i < entries.Count; i++)
        {
            Assert.Equal(entries[i - 1].End, entries[i].Start);
        }
    }

    [Theory]
    [InlineData(PlaybackMode.Shuffle)]
    [InlineData(PlaybackMode.SeededShuffle)]
    public void GetEntries_MatchGetEntryAt(PlaybackMode mode)
    {
        var engine = new ScheduleEngine(mode, 7, Start, Many(15));

        foreach (var entry in engine.GetEntries(Start.AddDays(2), Start.AddDays(4)))
        {
            var probe = engine.GetEntryAt(entry.Start.AddTicks(entry.Item.DurationTicks / 2))!;
            Assert.Equal(entry, probe);
        }
    }

    [Fact]
    public void SeededShuffle_IsDeterministicAndFixedAcrossCycles()
    {
        var items = Many(20);
        var first = new ScheduleEngine(PlaybackMode.SeededShuffle, 42, Start, items);
        var second = new ScheduleEngine(PlaybackMode.SeededShuffle, 42, Start, items);

        var cycle0 = CycleOrder(first, 0);
        Assert.Equal(cycle0, CycleOrder(second, 0));
        Assert.Equal(cycle0, CycleOrder(first, 5));
        Assert.NotEqual(items.Select(i => i.Title), cycle0);
        Assert.NotEqual(cycle0, CycleOrder(new ScheduleEngine(PlaybackMode.SeededShuffle, 43, Start, items), 0));
    }

    [Fact]
    public void Shuffle_ChangesEachCycleButKeepsEveryItem()
    {
        var items = Many(20);
        var engine = new ScheduleEngine(PlaybackMode.Shuffle, 42, Start, items);
        var again = new ScheduleEngine(PlaybackMode.Shuffle, 42, Start, items);

        var cycle0 = CycleOrder(engine, 0);
        var cycle1 = CycleOrder(engine, 1);

        Assert.NotEqual(cycle0, cycle1);
        Assert.Equal(items.Select(i => i.Title).Order(), cycle0.Order());
        Assert.Equal(items.Select(i => i.Title).Order(), cycle1.Order());
        Assert.Equal(cycle1, CycleOrder(again, 1));
    }

    [Fact]
    public void Shuffle_AlgorithmIsPinned()
    {
        // Si este test falla, las parrillas existentes cambiarían al actualizar el plugin.
        var shuffled = StableRandom.Shuffle(Enumerable.Range(0, 10).ToArray(), 42);

        Assert.Equal(PinnedShuffle, shuffled);
        Assert.Equal(7685912058256497202L, StableRandom.Hash("canal-1"));
    }

    [Fact]
    public void ForChannel_UsesChannelSettings()
    {
        var channel = new Channel { Id = "canal-1", Mode = PlaybackMode.Sequential, StartDate = Start.AddMinutes(5) };

        var engine = ScheduleEngine.ForChannel(channel, [A, B]);

        Assert.Equal("A", engine.GetEntryAt(Start.AddMinutes(5))!.Item.Title);
        Assert.Equal("B", engine.GetEntryAt(Start.AddMinutes(4))!.Item.Title);
    }

    private static List<string> CycleOrder(ScheduleEngine engine, int cycle)
    {
        var cycleStart = Start + TimeSpan.FromTicks(engine.CycleTicks * cycle);
        return engine.GetEntries(cycleStart, cycleStart + TimeSpan.FromTicks(engine.CycleTicks - 1))
            .Select(e => e.Item.Title)
            .ToList();
    }
}
