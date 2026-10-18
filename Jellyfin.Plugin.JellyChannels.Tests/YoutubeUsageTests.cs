using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.JellyChannels.Models;
using Jellyfin.Plugin.JellyChannels.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using static Jellyfin.Plugin.JellyChannels.Tests.TestItems;

namespace Jellyfin.Plugin.JellyChannels.Tests;

public sealed class YoutubeUsageTests : IDisposable
{
    private const string Y1 = "aaaaaaaaaaa";
    private const string Y2 = "bbbbbbbbbbb";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "jellychannels-yt-" + Guid.NewGuid().ToString("N"));
    private readonly ChannelStore _store;
    private readonly FakeResolver _resolver = new();
    private readonly YoutubeChannelUsage _usage;

    public YoutubeUsageTests()
    {
        _store = new ChannelStore(_folder, NullLogger<ChannelStore>.Instance);
        _usage = new YoutubeChannelUsage(_store, new ScheduleService(_store, _resolver, new FakeTimeProvider(Start)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, true);
        }
    }

    private static ScheduleItem Video(string id, double minutes) => Item("Vídeo " + id, minutes) with { YoutubeId = id, Kind = "YouTube" };

    private Channel Create(string name, params ChannelSource[] sources)
    {
        var channel = new Channel { Name = name, StartDate = Start };
        foreach (var source in sources)
        {
            channel.Sources.Add(source);
        }

        return _store.Create(channel);
    }

    [Fact]
    public void References_ListYoutubeSourcesWithTheirRetention()
    {
        Create(
            "Canal",
            new ChannelSource { Type = SourceType.Youtube, Url = "https://youtu.be/" + Y1, Retention = "keep" },
            new ChannelSource { Type = SourceType.Youtube, Url = Y2 },
            new ChannelSource { Type = SourceType.Tag, Value = "80s" });

        var references = _usage.GetReferences();

        Assert.Equal(
            [new YoutubeReference(Y1, new YoutubeRetention(YoutubeRetentionKind.Keep)), new YoutubeReference(Y2, YoutubeRetention.Default)],
            references);
    }

    [Fact]
    public void Needs_AreTheYoutubeSlotsInsideTheWindowIncludingTheOneOnAir()
    {
        var channel = Create("Canal", new ChannelSource { Type = SourceType.Youtube, Url = Y1 });
        _resolver.Items[channel.Id] = [Item("Biblioteca", 10), Video(Y1, 20), Video(Y2, 20)];
        var now = Start.AddMinutes(15);

        var needs = _usage.GetNeeds(now, TimeSpan.FromMinutes(10));
        var wider = _usage.GetNeeds(now, TimeSpan.FromMinutes(30));

        Assert.Equal([new YoutubeNeed(Y1, Start.AddMinutes(10), Start.AddMinutes(30))], needs);
        Assert.Equal([Y1, Y2], wider.Select(n => n.VideoId));
        Assert.Equal(Start.AddMinutes(30), wider[1].Start);
    }

    [Fact]
    public void Needs_RepeatOnEveryCycleInsideTheWindow()
    {
        var channel = Create("Canal", new ChannelSource { Type = SourceType.Youtube, Url = Y1 });
        _resolver.Items[channel.Id] = [Video(Y1, 10), Item("Biblioteca", 10)];

        var needs = _usage.GetNeeds(Start.AddMinutes(5), TimeSpan.FromMinutes(55));

        Assert.Equal([Start, Start.AddMinutes(20), Start.AddMinutes(40)], needs.Select(n => n.Start));
    }

    [Fact]
    public void Needs_SkipChannelsWithoutYoutubeSources()
    {
        var channel = Create("Canal", new ChannelSource { Type = SourceType.Tag, Value = "80s" });
        _resolver.Items[channel.Id] = [Item("Biblioteca", 10)];

        Assert.Empty(_usage.GetNeeds(Start, TimeSpan.FromHours(1)));
        Assert.Equal(0, _resolver.Calls);
    }

    [Fact]
    public void Validator_AcceptsAndRejectsYoutubeSources()
    {
        var good = new Channel { Name = "ok" };
        good.Sources.Add(new ChannelSource { Type = SourceType.Youtube, Url = "https://www.youtube.com/watch?v=" + Y1, Retention = "keep-days:7" });
        good.Sources.Add(new ChannelSource { Type = SourceType.Youtube, Url = Y2 });

        var bad = new Channel { Name = "mal" };
        bad.Sources.Add(new ChannelSource { Type = SourceType.Youtube, Url = "https://www.youtube.com/playlist?list=PLabcdefghijk" });
        bad.Sources.Add(new ChannelSource { Type = SourceType.Youtube, Url = "https://vimeo.com/123" });
        bad.Sources.Add(new ChannelSource { Type = SourceType.Youtube, Url = Y1, Retention = "para-siempre" });

        Assert.Empty(ChannelValidator.Validate(good));
        var errors = ChannelValidator.Validate(bad);
        Assert.Equal(3, errors.Count);
        Assert.Contains("listas de reproducción", errors[0], StringComparison.Ordinal);
        Assert.Contains("retention", errors[2], StringComparison.Ordinal);
    }

    private sealed class FakeResolver : ILibraryResolver
    {
        public Dictionary<string, IReadOnlyList<ScheduleItem>> Items { get; } = [];

        public int Calls { get; private set; }

        public IReadOnlyList<ScheduleItem> Resolve(Channel channel)
        {
            Calls++;
            return Items.GetValueOrDefault(channel.Id, []);
        }
    }
}
