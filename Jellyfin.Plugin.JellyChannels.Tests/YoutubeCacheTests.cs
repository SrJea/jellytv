using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyChannels.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Jellyfin.Plugin.JellyChannels.Tests;

public sealed class YoutubeCacheTests : IDisposable
{
    private const string Id = "dQw4w9WgXcQ";
    private const string Other = "9bZkp7q19f0";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "jellychannels-yt-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeYtDlp _yt = new();
    private YoutubeSettings _settings = new(true, "b[ext=mp4]/b", 0, TimeSpan.FromMinutes(30));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, true);
        }
    }

    private static string Meta(string id = Id, double duration = 212, string extra = "")
        => $$"""{"id":"{{id}}","title":"Vídeo {{id}}","duration":{{duration.ToString(System.Globalization.CultureInfo.InvariantCulture)}},"thumbnail":"https://i.ytimg.com/vi/{{id}}/hq.jpg","description":"Descripción","channel":"Canal X","ext":"mp4","vcodec":"avc1.64001F","acodec":"mp4a.40.2"{{extra}}}""";

    private YoutubeCache NewCache() => new(_folder, _yt, NullLogger<YoutubeCache>.Instance, _time, () => _settings);

    private static YoutubeReference Ref(string id, string? retention = null)
    {
        YoutubeRetention.TryParse(retention, out var parsed);
        return new YoutubeReference(id, parsed);
    }

    private YoutubeNeed Need(string id) => new(id, _time.GetUtcNow(), _time.GetUtcNow().AddMinutes(10));

    private async Task<YoutubeCache> CachedAsync(string id = Id, string? retention = null, int size = 1000)
    {
        var cache = NewCache();
        cache.Maintain([Ref(id, retention)], [], _time.GetUtcNow());
        _yt.Handler = args => args.Contains("--dump-single-json") ? new YtDlpResult(0, Meta(id), string.Empty) : _yt.Download(args, id, size);
        Assert.True(await cache.FetchMetadataAsync(id, CancellationToken.None));
        Assert.True(await cache.DownloadAsync(id, CancellationToken.None));
        return cache;
    }

    [Fact]
    public async Task Metadata_IsReadWithoutDownloadingAndTurnedIntoAScheduleItem()
    {
        var cache = NewCache();
        _yt.Handler = _ => new YtDlpResult(0, Meta(), string.Empty);

        Assert.True(await cache.FetchMetadataAsync(Id, CancellationToken.None));

        var args = _yt.Calls.Single();
        Assert.Contains("--skip-download", args);
        Assert.Contains("--dump-single-json", args);
        Assert.Contains("--no-playlist", args);
        Assert.Equal("b[ext=mp4]/b", args[args.ToList().IndexOf("-f") + 1]);
        Assert.Equal(["--", "https://www.youtube.com/watch?v=" + Id], args.TakeLast(2));
        Assert.False(Directory.Exists(cache.CacheFolder) && Directory.EnumerateFiles(cache.CacheFolder).Any());

        var item = cache.GetScheduleItem(Id)!;
        Assert.Equal("Vídeo " + Id, item.Title);
        Assert.Equal(TimeSpan.FromSeconds(212).Ticks, item.DurationTicks);
        Assert.Equal(Id, item.YoutubeId);
        Assert.Equal(YoutubeId.ToGuid(Id), item.ItemId);
        Assert.Equal("https://i.ytimg.com/vi/" + Id + "/hq.jpg", item.ImageUrl);
        Assert.Equal("YouTube", item.Kind);
        Assert.Null(item.Path);
    }

    [Fact]
    public async Task ScheduleItem_IsNullWhileDisabledOrWithoutMetadata()
    {
        var cache = NewCache();
        Assert.Null(cache.GetScheduleItem(Id));

        _yt.Handler = _ => new YtDlpResult(0, Meta(), string.Empty);
        await cache.FetchMetadataAsync(Id, CancellationToken.None);
        Assert.NotNull(cache.GetScheduleItem(Id));

        _settings = _settings with { Enabled = false };
        Assert.Null(cache.GetScheduleItem(Id));
    }

    [Theory]
    [InlineData("""{"id":"x","title":"Sin duración"}""", "duración")]
    [InlineData("""{"id":"x","title":"Directo","duration":10,"is_live":true}""", "directo")]
    [InlineData("""{"id":"x","title":"Próximo","duration":10,"live_status":"is_upcoming"}""", "directo")]
    [InlineData("""{"id":"x","title":"Cero","duration":0}""", "duración")]
    [InlineData("esto no es json", "JSON")]
    [InlineData("[1,2]", "objeto")]
    public async Task Metadata_RejectsWhatCannotBeScheduled(string json, string reason)
    {
        var cache = NewCache();
        _yt.Handler = _ => new YtDlpResult(0, json, string.Empty);

        Assert.False(await cache.FetchMetadataAsync(Id, CancellationToken.None));

        var video = cache.Get(Id)!;
        Assert.Null(video.Title);
        Assert.Equal(1, video.Failures);
        Assert.Contains(reason, video.LastError, StringComparison.OrdinalIgnoreCase);
        Assert.Null(cache.GetScheduleItem(Id));
    }

    [Fact]
    public async Task Metadata_DropsThumbnailsThatAreNotWebUrls()
    {
        var cache = NewCache();
        _yt.Handler = _ => new YtDlpResult(0, """{"id":"x","title":"T","duration":10,"thumbnail":"file:///etc/passwd"}""", string.Empty);

        await cache.FetchMetadataAsync(Id, CancellationToken.None);

        Assert.Null(cache.Get(Id)!.Thumbnail);
        Assert.NotNull(cache.GetScheduleItem(Id));
    }

    [Fact]
    public async Task FailedFetch_BacksOffExponentiallyAndCapsAtSixHours()
    {
        var cache = NewCache();
        _yt.Handler = _ => new YtDlpResult(1, string.Empty, "WARNING: x\nERROR: [youtube] dQw4w9WgXcQ: Video unavailable");
        cache.Maintain([Ref(Id)], [], _time.GetUtcNow());
        var delays = new List<TimeSpan>();

        for (int i = 0; i < 12; i++)
        {
            var before = _time.GetUtcNow();
            await cache.FetchMetadataAsync(Id, CancellationToken.None);
            delays.Add(cache.Get(Id)!.NextAttemptAt!.Value - before);
        }

        Assert.Equal(TimeSpan.FromMinutes(5), delays[0]);
        Assert.Equal(TimeSpan.FromMinutes(10), delays[1]);
        Assert.Equal(TimeSpan.FromMinutes(20), delays[2]);
        Assert.Equal(TimeSpan.FromHours(6), delays[^1]);
        Assert.Equal("ERROR: [youtube] dQw4w9WgXcQ: Video unavailable", cache.Get(Id)!.LastError);
    }

    [Fact]
    public async Task Maintain_WaitsForTheBackoffBeforeRetrying()
    {
        var cache = NewCache();
        _yt.Handler = _ => new YtDlpResult(1, string.Empty, "ERROR: boom");
        await cache.FetchMetadataAsync(Id, CancellationToken.None);

        Assert.Empty(cache.Maintain([Ref(Id)], [], _time.GetUtcNow()).MetadataToFetch);
        _time.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal([Id], cache.Maintain([Ref(Id)], [], _time.GetUtcNow()).MetadataToFetch);

        cache.ResetFailures(Id);
        Assert.Equal([Id], cache.Maintain([Ref(Id)], [], _time.GetUtcNow()).MetadataToFetch);
    }

    [Fact]
    public async Task Download_FetchesToTheCacheFolderAndRemembersTheFile()
    {
        var cache = await CachedAsync();

        var path = cache.GetFilePath(Id)!;
        Assert.Equal(Path.Combine(cache.CacheFolder, Id + ".mp4"), path);
        Assert.True(File.Exists(path));
        var args = _yt.Calls.Last();
        Assert.Equal(["--", "https://www.youtube.com/watch?v=" + Id], args.TakeLast(2));
        Assert.Contains("after_move:filepath", args);
        Assert.StartsWith(cache.CacheFolder, args[args.ToList().IndexOf("-o") + 1], StringComparison.Ordinal);
        Assert.Equal(1000, cache.Get(Id)!.SizeBytes);

        int calls = _yt.Calls.Count;
        Assert.True(await cache.DownloadAsync(Id, CancellationToken.None));
        Assert.Equal(calls, _yt.Calls.Count);
    }

    [Fact]
    public async Task Download_DoesNothingWithoutMetadata()
    {
        var cache = NewCache();

        Assert.False(await cache.DownloadAsync(Id, CancellationToken.None));
        Assert.Empty(_yt.Calls);
    }

    [Fact]
    public async Task Download_FailureCleansPartialFilesAndBacksOff()
    {
        var cache = NewCache();
        _yt.Handler = _ => new YtDlpResult(0, Meta(), string.Empty);
        await cache.FetchMetadataAsync(Id, CancellationToken.None);
        Directory.CreateDirectory(cache.CacheFolder);
        await File.WriteAllTextAsync(Path.Combine(cache.CacheFolder, Id + ".mp4.part"), "parcial");
        await File.WriteAllTextAsync(Path.Combine(cache.CacheFolder, Other + ".mp4"), "de otro vídeo");
        _yt.Handler = _ => new YtDlpResult(1, string.Empty, "ERROR: HTTP Error 403");

        Assert.False(await cache.DownloadAsync(Id, CancellationToken.None));

        Assert.Null(cache.GetFilePath(Id));
        Assert.False(File.Exists(Path.Combine(cache.CacheFolder, Id + ".mp4.part")));
        Assert.True(File.Exists(Path.Combine(cache.CacheFolder, Other + ".mp4")));
        Assert.Equal("ERROR: HTTP Error 403", cache.Get(Id)!.LastError);
        Assert.Empty(cache.Maintain([Ref(Id)], [Need(Id)], _time.GetUtcNow()).ToDownload);
    }

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("../fuera.mp4")]
    [InlineData("OTRO_VIDEO1.mp4")]
    [InlineData("")]
    public async Task Download_RejectsPathsThatYtDlpReportsOutsideTheCache(string reported)
    {
        var cache = NewCache();
        _yt.Handler = _ => new YtDlpResult(0, Meta(), string.Empty);
        await cache.FetchMetadataAsync(Id, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(_folder, "fuera.mp4"), "x");
        _yt.Handler = _ => new YtDlpResult(0, reported + "\n", string.Empty);

        Assert.False(await cache.DownloadAsync(Id, CancellationToken.None));

        Assert.Null(cache.GetFilePath(Id));
    }

    [Fact]
    public async Task GetFilePath_ForgetsFilesDeletedBehindTheBack()
    {
        var cache = await CachedAsync();
        File.Delete(cache.GetFilePath(Id)!);

        Assert.Null(cache.GetFilePath(Id));
        Assert.Null(cache.Get(Id)!.File);
        Assert.Equal([Id], cache.Maintain([Ref(Id)], [Need(Id)], _time.GetUtcNow()).ToDownload);
    }

    [Fact]
    public async Task PlaybackSource_UsesTheCodecsYtDlpAnnounced()
    {
        var cache = await CachedAsync();

        var source = cache.GetPlaybackSource(Id)!;

        Assert.Equal(cache.GetFilePath(Id), source.Path);
        Assert.Equal("h264", source.Video!.Codec);
        Assert.Equal("aac", source.Audio!.Codec);
        Assert.True(source.StreamsKnown);
        Assert.Equal(RemuxPlan.Copy, RemuxPlan.Decide(source, allowTranscode: true));
    }

    [Theory]
    [InlineData("avc1.64001F", "h264")]
    [InlineData("vp09.00.40.08", "vp9")]
    [InlineData("vp9", "vp9")]
    [InlineData("av01.0.05M.08", "av1")]
    [InlineData("hev1.1.6.L93", "hevc")]
    [InlineData("none", null)]
    [InlineData(null, null)]
    public void VideoCodecs_AreNormalizedToFfmpegNames(string? input, string? expected)
        => Assert.Equal(expected, YoutubeCache.NormalizeVideoCodec(input));

    [Theory]
    [InlineData("mp4a.40.2", "aac")]
    [InlineData("opus", "opus")]
    [InlineData("ac-3", "ac3")]
    [InlineData("ec-3", "eac3")]
    [InlineData("none", null)]
    public void AudioCodecs_AreNormalizedToFfmpegNames(string? input, string? expected)
        => Assert.Equal(expected, YoutubeCache.NormalizeAudioCodec(input));

    [Fact]
    public async Task Maintain_DownloadsOnlyWhatIsNeededSoonAndHasMetadata()
    {
        var cache = NewCache();
        _yt.Handler = _ => new YtDlpResult(0, Meta(), string.Empty);
        await cache.FetchMetadataAsync(Id, CancellationToken.None);

        var plan = cache.Maintain([Ref(Id), Ref(Other)], [Need(Id), Need(Other)], _time.GetUtcNow());

        Assert.Equal([Id], plan.ToDownload);
        Assert.Equal([Other], plan.MetadataToFetch);
    }

    [Fact]
    public async Task Maintain_DoesNothingWhileDisabledAndDeletesNothingNeeded()
    {
        var cache = await CachedAsync();
        _settings = _settings with { Enabled = false };

        var plan = cache.Maintain([Ref(Id)], [Need(Id)], _time.GetUtcNow());

        Assert.Empty(plan.ToDownload);
        Assert.Empty(plan.MetadataToFetch);
        Assert.Empty(plan.Deleted);
        Assert.NotNull(cache.GetFilePath(Id));
    }

    [Fact]
    public async Task DeleteAfterAiring_KeepsTheFileWhileItIsNeededAndDeletesItAfterwards()
    {
        var cache = await CachedAsync();
        var path = cache.GetFilePath(Id)!;

        Assert.Empty(cache.Maintain([Ref(Id)], [Need(Id)], _time.GetUtcNow()).Deleted);
        Assert.True(File.Exists(path));

        var plan = cache.Maintain([Ref(Id)], [], _time.GetUtcNow());

        Assert.Equal([Id], plan.Deleted);
        Assert.False(File.Exists(path));
        Assert.Null(cache.GetFilePath(Id));
        Assert.NotNull(cache.GetScheduleItem(Id));
    }

    [Fact]
    public async Task KeepDays_DeletesOnlyOnceTheDaysHavePassedSinceItWasLastNeeded()
    {
        var cache = await CachedAsync(retention: "keep-days:3");
        cache.Maintain([Ref(Id, "keep-days:3")], [Need(Id)], _time.GetUtcNow());
        _time.Advance(TimeSpan.FromDays(2.9));

        Assert.Empty(cache.Maintain([Ref(Id, "keep-days:3")], [], _time.GetUtcNow()).Deleted);
        _time.Advance(TimeSpan.FromDays(0.2));

        Assert.Equal([Id], cache.Maintain([Ref(Id, "keep-days:3")], [], _time.GetUtcNow()).Deleted);
    }

    [Fact]
    public async Task Keep_NeverDeletesAutomatically()
    {
        var cache = await CachedAsync(retention: "keep");
        _time.Advance(TimeSpan.FromDays(400));

        Assert.Empty(cache.Maintain([Ref(Id, "keep")], [], _time.GetUtcNow()).Deleted);
        Assert.NotNull(cache.GetFilePath(Id));
    }

    [Fact]
    public async Task SeveralChannelsUsingTheSameVideo_TheMostConservativeRetentionWins()
    {
        var cache = await CachedAsync();

        var plan = cache.Maintain([Ref(Id), Ref(Id, "keep")], [], _time.GetUtcNow());

        Assert.Empty(plan.Deleted);
        Assert.Equal("keep", cache.Get(Id)!.Retention);
    }

    [Fact]
    public async Task OrphanedVideos_KeepFollowingTheirLastRetention()
    {
        var kept = await CachedAsync(retention: "keep");
        Assert.Empty(kept.Maintain([], [], _time.GetUtcNow()).Deleted);
        Assert.NotNull(kept.Get(Id));

        var gone = await CachedAsync(Other);
        var plan = gone.Maintain([], [], _time.GetUtcNow());

        Assert.Contains(Other, plan.Deleted);
        Assert.Null(gone.Get(Other));
    }

    [Fact]
    public async Task DiskLimit_DeletesTheOldestAiredVideosFirstAndNeverTheKeptOrNeededOnes()
    {
        _settings = _settings with { MaxCacheBytes = 2500 };
        string[] ids = ["aaaaaaaaaaa", "bbbbbbbbbbb", "ccccccccccc", "ddddddddddd"];
        var cache = NewCache();
        cache.Maintain(ids.Select(i => Ref(i, i == "aaaaaaaaaaa" ? "keep" : "keep-days:30")), [], _time.GetUtcNow());
        foreach (var id in ids)
        {
            _yt.Handler = args => args.Contains("--dump-single-json") ? new YtDlpResult(0, Meta(id), string.Empty) : _yt.Download(args, id, 1000);
            await cache.FetchMetadataAsync(id, CancellationToken.None);
            _settings = _settings with { MaxCacheBytes = 0 };
            await cache.DownloadAsync(id, CancellationToken.None);
            cache.Maintain(ids.Select(i => Ref(i, i == "aaaaaaaaaaa" ? "keep" : "keep-days:30")), [Need(id)], _time.GetUtcNow());
            _time.Advance(TimeSpan.FromHours(1));
        }

        _settings = _settings with { MaxCacheBytes = 2500 };
        var plan = cache.Maintain(ids.Select(i => Ref(i, i == "aaaaaaaaaaa" ? "keep" : "keep-days:30")), [Need("ddddddddddd")], _time.GetUtcNow());

        Assert.Equal(["bbbbbbbbbbb", "ccccccccccc"], plan.Deleted.Order());
        Assert.NotNull(cache.GetFilePath("aaaaaaaaaaa"));
        Assert.NotNull(cache.GetFilePath("ddddddddddd"));
        Assert.Null(cache.GetFilePath("bbbbbbbbbbb"));
    }

    [Fact]
    public async Task Download_IsRefusedWhenTheCacheIsFullOfThingsThatCannotBeDeleted()
    {
        var cache = await CachedAsync(retention: "keep", size: 3000);
        _settings = _settings with { MaxCacheBytes = 2000 };
        cache.Maintain([Ref(Id, "keep"), Ref(Other)], [], _time.GetUtcNow());
        _yt.Handler = _ => new YtDlpResult(0, Meta(Other), string.Empty);
        await cache.FetchMetadataAsync(Other, CancellationToken.None);
        int calls = _yt.Calls.Count;

        Assert.False(await cache.DownloadAsync(Other, CancellationToken.None));

        Assert.Equal(calls, _yt.Calls.Count);
        Assert.Contains("llena", cache.Get(Other)!.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ManualDelete_RemovesTheFileButKeepsTheMetadata()
    {
        var cache = await CachedAsync(retention: "keep");
        var path = cache.GetFilePath(Id)!;

        Assert.True(cache.DeleteFile(Id));

        Assert.False(File.Exists(path));
        Assert.NotNull(cache.GetScheduleItem(Id));
        Assert.False(cache.DeleteFile(Id));
        Assert.False(cache.DeleteFile(Other));
    }

    [Fact]
    public async Task State_SurvivesARestart()
    {
        var first = await CachedAsync(retention: "keep-days:5");

        var second = NewCache();

        var video = second.Get(Id)!;
        Assert.Equal("Vídeo " + Id, video.Title);
        Assert.Equal("keep-days:5", video.Retention);
        Assert.Equal(first.GetFilePath(Id), second.GetFilePath(Id));
    }

    [Fact]
    public void Maintain_DoesNotRewriteTheStateFileWhenNothingChanged()
    {
        var cache = NewCache();
        cache.Maintain([Ref(Id)], [], _time.GetUtcNow());
        var stateFile = Path.Combine(_folder, "youtube.json");
        Assert.True(File.Exists(stateFile));
        File.Delete(stateFile);

        cache.Maintain([Ref(Id)], [], _time.GetUtcNow());

        Assert.False(File.Exists(stateFile));
    }

    [Fact]
    public async Task Maintain_RefreshesLastNeededAtOnlyEveryFewMinutes()
    {
        var cache = await CachedAsync();
        var first = _time.GetUtcNow();
        cache.Maintain([Ref(Id)], [Need(Id)], first);

        _time.Advance(TimeSpan.FromMinutes(1));
        cache.Maintain([Ref(Id)], [Need(Id)], _time.GetUtcNow());
        Assert.Equal(first, cache.Get(Id)!.LastNeededAt);

        _time.Advance(TimeSpan.FromMinutes(5));
        cache.Maintain([Ref(Id)], [Need(Id)], _time.GetUtcNow());
        Assert.Equal(_time.GetUtcNow(), cache.Get(Id)!.LastNeededAt);
    }

    [Fact]
    public async Task ChangedEvent_FiresWhenSomethingVisibleChanges()
    {
        var cache = NewCache();
        int changes = 0;
        cache.Changed += (_, _) => changes++;
        _yt.Handler = args => args.Contains("--dump-single-json") ? new YtDlpResult(0, Meta(), string.Empty) : _yt.Download(args, Id, 10);

        await cache.FetchMetadataAsync(Id, CancellationToken.None);
        await cache.DownloadAsync(Id, CancellationToken.None);
        cache.DeleteFile(Id);

        Assert.Equal(3, changes);
    }

    private sealed class FakeYtDlp : IYtDlpRunner
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];

        public Func<IReadOnlyList<string>, YtDlpResult> Handler { get; set; } = _ => new YtDlpResult(1, string.Empty, "sin configurar");

        public Task<YtDlpResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Calls.Add(arguments);
            return Task.FromResult(Handler(arguments));
        }

        /// <summary>
        /// Imita una descarga correcta de yt-dlp: escribe el fichero y devuelve su ruta.
        /// </summary>
        public YtDlpResult Download(IReadOnlyList<string> args, string id, int size)
        {
            var template = args[args.ToList().IndexOf("-o") + 1];
            var path = template.Replace("%(id)s", id, StringComparison.Ordinal).Replace("%(ext)s", "mp4", StringComparison.Ordinal);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new byte[size]);
            return new YtDlpResult(0, path + "\n", string.Empty);
        }
    }
}
