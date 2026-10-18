using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Jellyfin.Plugin.JellyChannels.Models;
using Jellyfin.Plugin.JellyChannels.Services;
using Microsoft.Extensions.Logging.Abstractions;
using static Jellyfin.Plugin.JellyChannels.Tests.TestItems;

namespace Jellyfin.Plugin.JellyChannels.Tests;

public sealed class BugRegressionTests : IDisposable
{
    private static readonly GuideUrls Urls = new("http://jf.local:8096", null);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "jellychannels-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, true);
        }
    }

    private static string EpgFor(params ScheduleItem[] items)
    {
        var channel = new Channel { Id = "c", Name = "C", Number = 1, EpgDays = 1, StartDate = Start };
        return EpgGenerator.Generate([(channel, ScheduleEngine.ForChannel(channel, items))], Urls, Start);
    }

    [Fact]
    public void Epg_StripsCharactersThatAreInvalidInXml()
    {
        var item = Item("Titulo\u0001 raro", 30) with { Overview = "Sinopsis\u0008 con\u000B control", EpisodeTitle = "Ep\u001F" };

        var doc = XDocument.Parse(EpgFor(item));

        var programme = doc.Root!.Element("programme")!;
        Assert.Equal("Titulo raro", programme.Element("title")!.Value);
        Assert.Equal("Sinopsis con control", programme.Element("desc")!.Value);
    }

    [Theory]
    [InlineData(0, 3, ".2.", "S00E03")]
    [InlineData(2, 0, null, "S02E00")]
    [InlineData(null, 4, ".3.", "S00E04")]
    [InlineData(1, 1, "0.0.", "S01E01")]
    public void Epg_EpisodeNumbersAreNeverNegative(int? season, int episode, string? xmltvNs, string onscreen)
    {
        var doc = XDocument.Parse(EpgFor(Item("Serie", 30, season, episode)));

        var numbers = doc.Root!.Element("programme")!.Elements("episode-num").ToDictionary(e => e.Attribute("system")!.Value, e => e.Value);
        Assert.Equal(xmltvNs, numbers.GetValueOrDefault("xmltv_ns"));
        Assert.Equal(onscreen, numbers["onscreen"]);
    }

    [Fact]
    public void Epg_ShortItemsDoNotReportZeroMinutes()
    {
        var doc = XDocument.Parse(EpgFor(Item("Corto", 0.5)));

        Assert.Equal("1", doc.Root!.Element("programme")!.Element("length")!.Value);
    }

    [Fact]
    public void Create_DoesNotKeepChannelInMemoryWhenSaveFails()
    {
        Directory.CreateDirectory(_folder);
        var blocker = Path.Combine(_folder, "ocupado");
        File.WriteAllText(blocker, "no soy una carpeta");
        var store = new ChannelStore(Path.Combine(blocker, "datos"), NullLogger<ChannelStore>.Instance);

        Assert.ThrowsAny<IOException>(() => store.Create(new Channel { Name = "Canal" }));

        Assert.Empty(store.GetAll());
    }

    [Fact]
    public void Update_DoesNotKeepChangesInMemoryWhenSaveFails()
    {
        var store = new ChannelStore(_folder, NullLogger<ChannelStore>.Instance);
        var created = store.Create(new Channel { Name = "Original" });
        Directory.Delete(_folder, true);
        File.WriteAllText(_folder, "ahora es un fichero");

        Assert.ThrowsAny<IOException>(() => store.Update(created.Id, new Channel { Name = "Cambiado" }));

        Assert.Equal("Original", store.Get(created.Id)!.Name);
        File.Delete(_folder);
    }

    [Fact]
    public void Seeds_SurviveAJavaScriptJsonRoundTrip()
    {
        var store = new ChannelStore(_folder, NullLogger<ChannelStore>.Instance);

        var created = store.Create(new Channel { Name = "Canal" });
        var huge = store.Create(new Channel { Name = "Otro", Seed = long.MaxValue - 12345 });

        foreach (var seed in new[] { created.Seed!.Value, huge.Seed!.Value })
        {
            Assert.Equal(seed, (long)(double)seed);
        }
    }

    [Fact]
    public void Resolve_NeverReturnsOutOfRangeDefaultSeed()
    {
        for (int i = 0; i < 200; i++)
        {
            long seed = StableRandom.DefaultSeed("canal-" + i);
            Assert.InRange(seed, 0, StableRandom.MaxSafeSeed);
        }
    }

    [Fact]
    public void Refresh_ClearsTheEpgCacheToo()
    {
        var store = new ChannelStore(_folder, NullLogger<ChannelStore>.Instance);
        store.Create(new Channel { Name = "Canal", StartDate = Start });
        var resolver = new MutableResolver { Items = [Item("Antes", 30)] };
        var schedule = new ScheduleService(store, resolver, new Microsoft.Extensions.Time.Testing.FakeTimeProvider(Start));
        var guide = new GuideService(store, schedule);

        Assert.Contains("Antes", guide.GetEpg(Urls), StringComparison.Ordinal);
        resolver.Items = [Item("Despues", 30)];
        schedule.Invalidate();

        Assert.Contains("Despues", guide.GetEpg(Urls), StringComparison.Ordinal);
    }

    [Fact]
    public void EpgCache_IsBounded()
    {
        var store = new ChannelStore(_folder, NullLogger<ChannelStore>.Instance);
        var schedule = new ScheduleService(store, new MutableResolver(), new Microsoft.Extensions.Time.Testing.FakeTimeProvider(Start));
        var guide = new GuideService(store, schedule);

        for (int i = 0; i < 500; i++)
        {
            guide.GetEpg(new GuideUrls("http://host-" + i, null));
        }

        Assert.True(guide.CacheCount <= 16, "La caché crece sin límite: " + guide.CacheCount);
    }

    [Fact]
    public void Validate_RejectsSourcesThatCanNeverResolve()
    {
        var channel = new Channel
        {
            Name = "Canal",
            Sources =
            {
                new ChannelSource { Type = SourceType.Series, Id = "no-es-un-guid" },
                new ChannelSource { Type = SourceType.Movie },
                new ChannelSource { Type = SourceType.Tag, Value = " " },
                new ChannelSource { Type = SourceType.Youtube },
                new ChannelSource { Type = SourceType.Collection, Id = Guid.NewGuid().ToString("N") },
                new ChannelSource { Type = SourceType.Tag, Value = "80s" }
            }
        };

        var errors = ChannelValidator.Validate(channel);

        Assert.Equal(4, errors.Count);
        Assert.Empty(ChannelValidator.Validate(new Channel { Name = "Vacío" }));
    }

    private sealed class MutableResolver : ILibraryResolver
    {
        public IReadOnlyList<ScheduleItem> Items { get; set; } = [];

        public IReadOnlyList<ScheduleItem> Resolve(Channel channel) => Items;
    }
}
