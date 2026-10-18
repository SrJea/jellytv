using System;
using System.Collections.Generic;
using System.IO;
using Jellyfin.Plugin.JellyChannels.Models;
using Jellyfin.Plugin.JellyChannels.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Jellyfin.Plugin.JellyChannels.Tests;

public sealed class ScheduleServiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "jellychannels-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeResolver _resolver = new();
    private readonly FakeTimeProvider _time = new(TestItems.Start);
    private readonly ChannelStore _store;
    private readonly ScheduleService _service;

    public ScheduleServiceTests()
    {
        _store = new ChannelStore(_folder, NullLogger<ChannelStore>.Instance);
        _service = new ScheduleService(_store, _resolver, _time);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, true);
        }
    }

    [Fact]
    public void Schedule_IsCachedUntilTtlExpires()
    {
        var channel = _store.Create(new Channel { Name = "Canal" });

        var first = _service.GetSchedule(channel);
        Assert.Same(first, _service.GetSchedule(channel));
        Assert.Equal(1, _resolver.Calls);

        _time.Advance(TimeSpan.FromMinutes(11));
        Assert.NotSame(first, _service.GetSchedule(channel));
        Assert.Equal(2, _resolver.Calls);
    }

    [Fact]
    public void Schedule_IsRebuiltWhenChannelChanges()
    {
        var channel = _store.Create(new Channel { Name = "Canal" });
        _service.GetSchedule(channel);

        var updated = _store.Update(channel.Id, new Channel { Name = "Canal", Mode = PlaybackMode.Shuffle })!;
        _service.GetSchedule(updated);

        Assert.Equal(2, _resolver.Calls);
    }

    private sealed class FakeResolver : ILibraryResolver
    {
        public int Calls { get; private set; }

        public IReadOnlyList<ScheduleItem> Resolve(Channel channel)
        {
            Calls++;
            return TestItems.Many(3);
        }
    }
}
