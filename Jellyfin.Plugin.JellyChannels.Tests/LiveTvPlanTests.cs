using System.Linq;
using Jellyfin.Plugin.JellyChannels.Services;
using MediaBrowser.Model.LiveTv;

namespace Jellyfin.Plugin.JellyChannels.Tests;

public class LiveTvPlanTests
{
    private const string M3u = "http://127.0.0.1:8096/JellyChannels/channels.m3u?ApiKey=abc";
    private const string Epg = "http://127.0.0.1:8096/JellyChannels/epg.xml?ApiKey=abc";

    private static TunerHostInfo Tuner(string id, string url, string type = "m3u") => new() { Id = id, Url = url, Type = type, FriendlyName = "x" };

    private static ListingsProviderInfo Listings(string id, string path, string type = "xmltv") => new() { Id = id, Path = path, Type = type };

    [Fact]
    public void NothingRegistered_CreatesBoth()
    {
        var plan = LiveTvPlan.Decide([], [], M3u, Epg);

        Assert.Equal(LiveTvAction.Create, plan.Tuner);
        Assert.Equal(LiveTvAction.Create, plan.Listings);
        Assert.True(plan.HasChanges);
    }

    [Fact]
    public void AlreadyRegisteredWithTheRightUrls_ChangesNothing()
    {
        var plan = LiveTvPlan.Decide([Tuner("t", M3u)], [Listings("l", Epg)], M3u, Epg);

        Assert.Equal(LiveTvAction.None, plan.Tuner);
        Assert.Equal(LiveTvAction.None, plan.Listings);
        Assert.False(plan.HasChanges);
    }

    [Fact]
    public void ManuallyAddedEntries_AreAdoptedAndUpdatedInsteadOfDuplicated()
    {
        var manual = Tuner("t", "https://mi-servidor.example/JellyChannels/channels.m3u?ApiKey=vieja");
        var manualListings = Listings("l", "https://mi-servidor.example/JellyChannels/epg.xml?ApiKey=vieja");

        var plan = LiveTvPlan.Decide([manual], [manualListings], M3u, Epg);

        Assert.Equal(LiveTvAction.Update, plan.Tuner);
        Assert.Same(manual, plan.ExistingTuner);
        Assert.Equal(LiveTvAction.Update, plan.Listings);
        Assert.Same(manualListings, plan.ExistingListings);
        Assert.Empty(plan.DuplicateTunerIds);
    }

    [Fact]
    public void OtherPeoplesTunersAndGuides_AreLeftAlone()
    {
        var tuners = new[] { Tuner("a", "http://hdhomerun.local/lineup.m3u"), Tuner("b", "http://otro/JellyChannels/channels.m3u", type: "hdhomerun") };
        var providers = new[] { Listings("c", "http://epg.example/guide.xml"), Listings("d", "http://otro/JellyChannels/epg.xml", type: "schedulesdirect") };

        var plan = LiveTvPlan.Decide(tuners, providers, M3u, Epg);

        Assert.Equal(LiveTvAction.Create, plan.Tuner);
        Assert.Equal(LiveTvAction.Create, plan.Listings);
        Assert.Null(plan.ExistingTuner);
        Assert.Empty(plan.DuplicateTunerIds);
        Assert.Empty(plan.DuplicateListingIds);
    }

    [Fact]
    public void RepeatedEntries_KeepTheFirstAndFlagTheRestForDeletion()
    {
        var plan = LiveTvPlan.Decide(
            [Tuner("t1", M3u), Tuner("t2", M3u), Tuner("t3", M3u)],
            [Listings("l1", Epg), Listings("l2", Epg)],
            M3u,
            Epg);

        Assert.Equal("t1", plan.ExistingTuner!.Id);
        Assert.Equal(["t2", "t3"], plan.DuplicateTunerIds.Order());
        Assert.Equal(["l2"], plan.DuplicateListingIds);
        Assert.True(plan.HasChanges);
    }

    [Theory]
    [InlineData("http://127.0.0.1:8096/jellychannels/CHANNELS.M3U?ApiKey=x", true)]
    [InlineData("http://x/other.m3u", false)]
    [InlineData(null, false)]
    public void Recognizes_OurTunerByThePathOfItsUrl(string? url, bool expected)
        => Assert.Equal(expected, LiveTvPlan.IsOurTuner(new TunerHostInfo { Type = "M3U", Url = url! }));
}
