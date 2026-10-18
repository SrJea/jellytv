using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyChannels.Models;
using Jellyfin.Plugin.JellyChannels.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using static Jellyfin.Plugin.JellyChannels.Tests.TestItems;

namespace Jellyfin.Plugin.JellyChannels.Tests;

public class RemuxTests
{
    private static ScheduleItem Clip(string name, double minutes)
        => Item(name, minutes) with { Path = $"/media/{name}.mkv" };

    [Fact]
    public void Arguments_PlaceSeekBeforeInputAndLimitAfterIt()
    {
        var args = RemuxArguments.Build(new RemuxRequest("/media/Mi Serie/S01 \"E01\".mkv", TimeSpan.FromSeconds(12.3456), TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(1234.5)));

        int re = args.ToList().IndexOf("-re");
        int ss = args.ToList().IndexOf("-ss");
        int input = args.ToList().IndexOf("-i");
        int limit = args.ToList().IndexOf("-t");
        Assert.True(re < ss && ss < input && input < limit);
        Assert.Equal("12.346", args[ss + 1]);
        Assert.Equal("/media/Mi Serie/S01 \"E01\".mkv", args[input + 1]);
        Assert.Equal("90.000", args[limit + 1]);
        Assert.Equal("1234.500", args[args.ToList().IndexOf("-output_ts_offset") + 1]);
        Assert.Equal("copy", args[args.ToList().IndexOf("-c") + 1]);
        Assert.Equal("pipe:1", args[^1]);
        Assert.DoesNotContain(args, a => a is "-c:v" or "-vcodec" or "-acodec" or "libx264");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(400)]
    public void Arguments_OmitSeekAndLimitWhenNotNeeded(long offsetTicks)
    {
        var args = RemuxArguments.Build(new RemuxRequest("/a.mkv", TimeSpan.FromTicks(offsetTicks), TimeSpan.Zero, TimeSpan.Zero));

        Assert.DoesNotContain("-ss", args);
        Assert.DoesNotContain("-t", args);
    }

    [Fact]
    public async Task Stream_StartsMidItemAndChainsTheNextOnesFromTheSchedule()
    {
        var h = new Harness(Clip("a", 1), Clip("b", 1), Clip("c", 1));
        h.Time.Advance(TimeSpan.FromSeconds(20));

        await h.RunAsync(stopAfterRequests: 3);

        Assert.Equal(["/media/a.mkv", "/media/b.mkv", "/media/c.mkv"], h.Runner.Requests.Select(r => r.Path));
        Assert.Equal(TimeSpan.FromSeconds(20), h.Runner.Requests[0].Offset);
        Assert.Equal(TimeSpan.FromSeconds(40), h.Runner.Requests[0].MaxDuration);
        Assert.Equal(TimeSpan.Zero, h.Runner.Requests[0].TimestampOffset);
        Assert.Equal(TimeSpan.Zero, h.Runner.Requests[1].Offset);
        Assert.Equal(TimeSpan.FromSeconds(60), h.Runner.Requests[1].MaxDuration);
        Assert.Equal(TimeSpan.FromSeconds(40), h.Runner.Requests[1].TimestampOffset);
        Assert.Equal(TimeSpan.FromSeconds(100), h.Runner.Requests[2].TimestampOffset);
    }

    [Fact]
    public async Task Stream_TimelineNeverGoesBackwardsWhenASegmentOverruns()
    {
        // Con -c copy el primer tramo puede empezar un GOP antes y durar más que su hueco.
        var h = new Harness(Clip("a", 1), Clip("b", 1), Clip("c", 1));
        h.Time.Advance(TimeSpan.FromSeconds(20));
        h.Runner.Duration = request => request.Path == "/media/a.mkv" ? TimeSpan.FromSeconds(45) : request.MaxDuration;

        await h.RunAsync(stopAfterRequests: 3);

        var second = h.Runner.Requests[1];
        Assert.Equal("/media/b.mkv", second.Path);
        Assert.Equal(TimeSpan.FromSeconds(5), second.Offset);
        for (int i = 1; i < h.Runner.Requests.Count; i++)
        {
            var previous = h.Runner.Requests[i - 1];
            Assert.True(
                h.Runner.Requests[i].TimestampOffset >= previous.TimestampOffset + h.Runner.Duration(previous),
                $"El tramo {i} empieza antes de que acabe el anterior");
        }
    }

    [Fact]
    public async Task Stream_StartsTheNextSegmentAfterTheLastTimestampActuallyWritten()
    {
        // -re no reparte el contenido previo al salto: el tramo dura 5 s más en PTS que en reloj.
        var h = new Harness(Clip("a", 1), Clip("b", 1));
        h.Time.Advance(TimeSpan.FromSeconds(20));
        h.Runner.Result = request => new RemuxResult(0, 1000, request.TimestampOffset + request.MaxDuration + TimeSpan.FromSeconds(5));

        await h.RunAsync(stopAfterRequests: 2);

        Assert.Equal(TimeSpan.FromSeconds(45).Add(TimeSpan.FromMilliseconds(40)), h.Runner.Requests[1].TimestampOffset);
    }

    [Fact]
    public async Task Stream_DoesNotLaunchFfmpegForTheLastFractionOfASlot()
    {
        var h = new Harness(Clip("a", 1), Clip("b", 1));
        h.Time.Advance(TimeSpan.FromSeconds(59.5));

        await h.RunAsync(stopAfterRequests: 1);

        Assert.Equal("/media/b.mkv", h.Runner.Requests[0].Path);
    }

    [Fact]
    public async Task Stream_RetriesThenSkipsAnUnplayableItem()
    {
        var h = new Harness(Clip("a", 1), Clip("b", 1));
        h.Runner.Result = request => request.Path == "/media/a.mkv" ? new RemuxResult(1, 0) : new RemuxResult(0, 100);

        await h.RunAsync(stopAfterRequests: 4);

        Assert.Equal(["/media/a.mkv", "/media/a.mkv", "/media/a.mkv", "/media/b.mkv"], h.Runner.Requests.Select(r => r.Path));
        Assert.True(h.Time.GetUtcNow() >= TestItems.Start.AddSeconds(60));
    }

    [Fact]
    public async Task Stream_GivesUpAfterSeveralUnplayableSlotsInARow()
    {
        var h = new Harness(Clip("a", 1), Clip("b", 1));
        h.Runner.Result = _ => new RemuxResult(1, 0);

        await h.RunAsync(stopAfterRequests: 1000);

        Assert.True(h.Runner.Requests.Count is >= 9 and <= 12, $"Se lanzaron {h.Runner.Requests.Count} tramos");
    }

    [Fact]
    public async Task Stream_SkipsItemsThatAreNotLocalFiles()
    {
        var h = new Harness(Item("remoto", 1), Clip("b", 1));

        await h.RunAsync(stopAfterRequests: 1);

        Assert.Equal("/media/b.mkv", h.Runner.Requests[0].Path);
    }

    [Fact]
    public async Task Stream_EndsImmediatelyForAnEmptyChannel()
    {
        var h = new Harness();

        await h.RunAsync(stopAfterRequests: 1);

        Assert.Empty(h.Runner.Requests);
    }

    [Fact]
    public void Slots_AreLimitedAndReleased()
    {
        int max = 2;
        var service = new RemuxService(new FakeRunner(new FakeTimeProvider()), NullLogger<RemuxService>.Instance, new FakeTimeProvider(), () => max);

        var first = service.TryEnter();
        var second = service.TryEnter();

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Null(service.TryEnter());
        Assert.Equal(2, service.ActiveStreams);

        first!.Dispose();
        first.Dispose();
        Assert.Equal(1, service.ActiveStreams);
        Assert.NotNull(service.TryEnter());
        max = 5;
        Assert.NotNull(service.TryEnter());
    }

    private sealed class Harness
    {
        private readonly ScheduleEngine _engine;

        public Harness(params ScheduleItem[] items)
        {
            Time = new FakeTimeProvider(TestItems.Start);
            Runner = new FakeRunner(Time);
            Service = new RemuxService(Runner, NullLogger<RemuxService>.Instance, Time, () => 2);
            _engine = new ScheduleEngine(PlaybackMode.Sequential, 1, TestItems.Start, items);
        }

        public FakeTimeProvider Time { get; }

        public FakeRunner Runner { get; }

        public RemuxService Service { get; }

        public async Task RunAsync(int stopAfterRequests)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            Runner.OnRequest = count =>
            {
                if (count >= stopAfterRequests)
                {
                    cts.Cancel();
                }
            };

            var task = Service.StreamAsync(() => _engine, Stream.Null, cts.Token);
            while (!task.IsCompleted)
            {
                Time.Advance(TimeSpan.FromMilliseconds(250));
                await Task.Delay(1);
            }

            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private sealed class FakeRunner : IFfmpegRunner
    {
        private readonly FakeTimeProvider _time;

        public FakeRunner(FakeTimeProvider time)
        {
            _time = time;
            Duration = request => request.MaxDuration;
            Result = _ => new RemuxResult(0, 1000);
        }

        public List<RemuxRequest> Requests { get; } = [];

        public Func<RemuxRequest, TimeSpan> Duration { get; set; }

        public Func<RemuxRequest, RemuxResult> Result { get; set; }

        public Action<int>? OnRequest { get; set; }

        public bool IsAvailable => true;

        public Task<RemuxResult> RunAsync(RemuxRequest request, Stream output, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var result = Result(request);
            if (result.BytesWritten > 0)
            {
                _time.Advance(Duration(request));
            }

            OnRequest?.Invoke(Requests.Count);
            return Task.FromResult(result);
        }
    }
}
