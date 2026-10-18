using System;
using System.Linq;
using Jellyfin.Plugin.JellyChannels.Models;

namespace Jellyfin.Plugin.JellyChannels.Tests;

internal static class TestItems
{
    public static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static ScheduleItem Item(string title, double minutes, int? season = null, int? episode = null)
        => new()
        {
            ItemId = Guid.NewGuid(),
            Title = title,
            DurationTicks = TimeSpan.FromMinutes(minutes).Ticks,
            Season = season,
            Episode = episode,
            Kind = episode is null ? "Movie" : "Episode"
        };

    public static ScheduleItem[] Many(int count, double minutes = 22)
        => Enumerable.Range(0, count).Select(i => Item("Item " + i, minutes + i)).ToArray();
}
