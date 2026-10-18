using System;
using Jellyfin.Plugin.JellyChannels.Services;

namespace Jellyfin.Plugin.JellyChannels.Tests;

public class YoutubeIdTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("http://youtube.com/watch?v=dQw4w9WgXcQ&t=42s")]
    [InlineData("https://www.youtube.com/watch?list=PL123&v=dQw4w9WgXcQ")]
    [InlineData("https://m.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://music.youtube.com/watch?v=dQw4w9WgXcQ&list=RDAMVM")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ?si=abc")]
    [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/live/dQw4w9WgXcQ?feature=share")]
    [InlineData("https://www.youtube.com/embed/dQw4w9WgXcQ")]
    [InlineData("https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ")]
    [InlineData("  dQw4w9WgXcQ  ")]
    public void TryParse_AcceptsVideoUrlsAndBareIds(string input)
    {
        Assert.True(YoutubeId.TryParse(input, out var id));
        Assert.Equal("dQw4w9WgXcQ", id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://www.youtube.com/playlist?list=PL1234567890A")]
    [InlineData("https://www.youtube.com/@canal")]
    [InlineData("https://www.youtube.com/watch")]
    [InlineData("https://www.youtube.com/watch?v=corto")]
    [InlineData("https://vimeo.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtube.com.evil.example/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://evil.example/youtu.be/dQw4w9WgXcQ")]
    [InlineData("ftp://youtu.be/dQw4w9WgXcQ")]
    [InlineData("dQw4w9WgXc")]
    [InlineData("dQw4w9WgXcQQ")]
    [InlineData("--exec=rm -rf")]
    [InlineData("dQw4w9WgXc;")]
    public void TryParse_RejectsEverythingElse(string? input)
        => Assert.False(YoutubeId.TryParse(input, out _));

    [Fact]
    public void CanonicalUrl_IsAlwaysTheWatchUrlOfTheId()
    {
        Assert.Equal("https://www.youtube.com/watch?v=dQw4w9WgXcQ", YoutubeId.CanonicalUrl("dQw4w9WgXcQ"));
        Assert.Throws<ArgumentException>(() => YoutubeId.CanonicalUrl("--exec=evil"));
    }

    [Fact]
    public void ToGuid_IsStableAndDistinctPerVideo()
    {
        Assert.Equal(YoutubeId.ToGuid("dQw4w9WgXcQ"), YoutubeId.ToGuid("dQw4w9WgXcQ"));
        Assert.NotEqual(YoutubeId.ToGuid("dQw4w9WgXcQ"), YoutubeId.ToGuid("9bZkp7q19f0"));
    }

    [Theory]
    [InlineData(null, YoutubeRetentionKind.DeleteAfterAiring, 0)]
    [InlineData("", YoutubeRetentionKind.DeleteAfterAiring, 0)]
    [InlineData("delete-after-airing", YoutubeRetentionKind.DeleteAfterAiring, 0)]
    [InlineData("KEEP", YoutubeRetentionKind.Keep, 0)]
    [InlineData("keep-days:7", YoutubeRetentionKind.KeepDays, 7)]
    [InlineData(" Keep-Days:3650 ", YoutubeRetentionKind.KeepDays, 3650)]
    public void Retention_ParsesTheDocumentedValues(string? text, YoutubeRetentionKind kind, int days)
    {
        Assert.True(YoutubeRetention.TryParse(text, out var retention));
        Assert.Equal(kind, retention.Kind);
        Assert.Equal(days, retention.Days);
    }

    [Theory]
    [InlineData("keep-days:0")]
    [InlineData("keep-days:-1")]
    [InlineData("keep-days:3651")]
    [InlineData("keep-days:abc")]
    [InlineData("keep-days:")]
    [InlineData("delete")]
    [InlineData("forever")]
    public void Retention_RejectsInvalidValues(string text)
        => Assert.False(YoutubeRetention.TryParse(text, out _));

    [Fact]
    public void Retention_MostConservativeWins()
    {
        var delete = YoutubeRetention.Default;
        var days3 = new YoutubeRetention(YoutubeRetentionKind.KeepDays, 3);
        var days9 = new YoutubeRetention(YoutubeRetentionKind.KeepDays, 9);
        var keep = new YoutubeRetention(YoutubeRetentionKind.Keep);

        Assert.Equal(delete, YoutubeRetention.MostConservative(delete, delete));
        Assert.Equal(days9, YoutubeRetention.MostConservative(days3, days9));
        Assert.Equal(days3, YoutubeRetention.MostConservative(delete, days3));
        Assert.Equal(keep, YoutubeRetention.MostConservative(days9, keep));
        Assert.Equal("keep-days:9", days9.ToText());
    }
}
