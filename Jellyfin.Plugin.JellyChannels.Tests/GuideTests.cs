using System;
using System.Linq;
using System.Xml.Linq;
using Jellyfin.Plugin.JellyChannels.Models;
using Jellyfin.Plugin.JellyChannels.Services;
using static Jellyfin.Plugin.JellyChannels.Tests.TestItems;

namespace Jellyfin.Plugin.JellyChannels.Tests;

public class GuideTests
{
    private static readonly GuideUrls Urls = new("http://jf.local:8096/jf", null);

    private static Channel RetroChannel() => new()
    {
        Id = "canal-retro",
        Name = "Canal \"Retro\"",
        Number = 7,
        Logo = "logo.png",
        EpgDays = 1,
        StartDate = Start
    };

    [Fact]
    public void Epg_ContainsChannelAndContiguousProgrammes()
    {
        var channel = RetroChannel();
        var episode = Item("Serie", 25, season: 1, episode: 5) with { EpisodeTitle = "Piloto <1>", Overview = "Sinopsis & más", Year = 1987, Genres = ["Comedia"], ImageItemId = Guid.Parse("11111111-2222-3333-4444-555555555555") };
        var engine = ScheduleEngine.ForChannel(channel, [episode, Item("Película", 95)]);
        var now = Start.AddMinutes(10);

        var doc = XDocument.Parse(EpgGenerator.Generate([(channel, engine)], Urls, now));

        var xmlChannel = doc.Root!.Element("channel")!;
        Assert.Equal("canal-retro.jellychannels", xmlChannel.Attribute("id")!.Value);
        Assert.Equal("Canal \"Retro\"", xmlChannel.Element("display-name")!.Value);
        Assert.Equal("http://jf.local:8096/jf/JellyChannels/assets/logo.png", xmlChannel.Element("icon")!.Attribute("src")!.Value);

        var programmes = doc.Root.Elements("programme").ToList();
        Assert.Equal("20260101000000 +0000", programmes[0].Attribute("start")!.Value);
        Assert.Equal("20260101002500 +0000", programmes[0].Attribute("stop")!.Value);
        for (int i = 1; i < programmes.Count; i++)
        {
            Assert.Equal(programmes[i - 1].Attribute("stop")!.Value, programmes[i].Attribute("start")!.Value);
        }

        Assert.True(DateTimeOffset.ParseExact(programmes[^1].Attribute("stop")!.Value, "yyyyMMddHHmmss zzz", null) >= now.AddDays(1));

        var first = programmes[0];
        Assert.Equal("Serie", first.Element("title")!.Value);
        Assert.Equal("Piloto <1>", first.Element("sub-title")!.Value);
        Assert.Equal("Sinopsis & más", first.Element("desc")!.Value);
        Assert.Equal("1987", first.Element("date")!.Value);
        Assert.Equal("Comedia", first.Element("category")!.Value);
        Assert.Equal("0.4.", first.Elements("episode-num").Single(e => e.Attribute("system")!.Value == "xmltv_ns").Value);
        Assert.Equal("S01E05", first.Elements("episode-num").Single(e => e.Attribute("system")!.Value == "onscreen").Value);
        Assert.Equal("http://jf.local:8096/jf/Items/11111111222233334444555555555555/Images/Primary", first.Element("icon")!.Attribute("src")!.Value);

        var movie = programmes[1];
        Assert.Null(movie.Element("sub-title"));
        Assert.Empty(movie.Elements("episode-num"));
    }

    [Fact]
    public void Epg_EmptyChannelHasNoProgrammes()
    {
        var channel = RetroChannel();

        var doc = XDocument.Parse(EpgGenerator.Generate([(channel, ScheduleEngine.ForChannel(channel, []))], Urls, Start));

        Assert.Single(doc.Root!.Elements("channel"));
        Assert.Empty(doc.Root.Elements("programme"));
    }

    [Fact]
    public void M3u_ListsChannelsWithTvgAttributes()
    {
        var m3u = M3uGenerator.Generate([RetroChannel(), new Channel { Id = "otro", Name = "Otro", Number = 8, Logo = "https://cdn.example/logo.png" }], Urls);
        var lines = m3u.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("#EXTM3U url-tvg=\"http://jf.local:8096/jf/JellyChannels/epg.xml\"", lines[0]);
        Assert.Equal("#EXTINF:-1 tvg-id=\"canal-retro.jellychannels\" tvg-name=\"Canal 'Retro'\" tvg-chno=\"7\" tvg-logo=\"http://jf.local:8096/jf/JellyChannels/assets/logo.png\" group-title=\"JellyChannels\",Canal \"Retro\"", lines[1]);
        Assert.Equal("http://jf.local:8096/jf/JellyChannels/channels/canal-retro/stream", lines[2]);
        Assert.Contains("tvg-logo=\"https://cdn.example/logo.png\"", lines[3], StringComparison.Ordinal);
    }

    [Fact]
    public void M3u_PropagatesApiKey()
    {
        var m3u = M3uGenerator.Generate([RetroChannel()], Urls with { ApiKey = "abc 123" });

        Assert.Contains("epg.xml?ApiKey=abc%20123", m3u, StringComparison.Ordinal);
        Assert.Contains("/stream?ApiKey=abc%20123", m3u, StringComparison.Ordinal);
    }
}
