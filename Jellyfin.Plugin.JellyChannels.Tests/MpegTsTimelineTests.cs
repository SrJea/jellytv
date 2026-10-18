using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.JellyChannels.Services;

namespace Jellyfin.Plugin.JellyChannels.Tests;

public class MpegTsTimelineTests
{
    private const long Hz = 90_000;

    private static byte[] PesPacket(long pts, byte streamId = 0xE0, bool payloadStart = true, int adaptationLength = -1, bool withPts = true)
    {
        var packet = new byte[188];
        Array.Fill(packet, (byte)0xFF);
        packet[0] = 0x47;
        packet[1] = (byte)((payloadStart ? 0x40 : 0) | 0x01);
        packet[2] = 0x00;
        int offset = 4;
        if (adaptationLength >= 0)
        {
            packet[3] = 0x30;
            packet[4] = (byte)adaptationLength;
            offset = 5 + adaptationLength;
        }
        else
        {
            packet[3] = 0x10;
        }

        packet[offset] = 0;
        packet[offset + 1] = 0;
        packet[offset + 2] = 1;
        packet[offset + 3] = streamId;
        packet[offset + 4] = 0;
        packet[offset + 5] = 0;
        packet[offset + 6] = 0x80;
        packet[offset + 7] = (byte)(withPts ? 0x80 : 0x00);
        packet[offset + 8] = (byte)(withPts ? 5 : 0);
        if (withPts)
        {
            packet[offset + 9] = (byte)(0x21 | ((pts >> 29) & 0x0E));
            packet[offset + 10] = (byte)((pts >> 22) & 0xFF);
            packet[offset + 11] = (byte)(((pts >> 14) & 0xFE) | 1);
            packet[offset + 12] = (byte)((pts >> 7) & 0xFF);
            packet[offset + 13] = (byte)(((pts << 1) & 0xFE) | 1);
        }

        return packet;
    }

    private static MpegTsTimeline Feed(params byte[][] packets)
    {
        var timeline = new MpegTsTimeline();
        foreach (var packet in packets)
        {
            timeline.Feed(packet);
        }

        return timeline;
    }

    [Fact]
    public void NothingSeenYet_HasNoTimestamp() => Assert.Null(new MpegTsTimeline().Last);

    [Fact]
    public void ReadsThePtsOfAPesPacket()
        => Assert.Equal(TimeSpan.FromSeconds(5), Feed(PesPacket(5 * Hz)).Last);

    [Fact]
    public void KeepsTheHighestPtsOfAnyAudioOrVideoStream()
    {
        var timeline = Feed(PesPacket(10 * Hz), PesPacket(12 * Hz, streamId: 0xC0), PesPacket(11 * Hz), PesPacket(7 * Hz, streamId: 0xBD));

        Assert.Equal(TimeSpan.FromSeconds(12), timeline.Last);
    }

    [Fact]
    public void Reads33BitTimestamps()
    {
        long pts = (1L << 32) + 90_000;

        Assert.Equal(TimeSpan.FromSeconds((double)pts / Hz), Feed(PesPacket(pts)).Last);
    }

    [Fact]
    public void SkipsAnAdaptationFieldBeforeThePes()
        => Assert.Equal(TimeSpan.FromSeconds(3), Feed(PesPacket(3 * Hz, adaptationLength: 20)).Last);

    [Fact]
    public void IgnoresPacketsThatAreNotTheStartOfAPesOrCarryNoPts()
    {
        var timeline = Feed(
            PesPacket(99 * Hz, payloadStart: false),
            PesPacket(98 * Hz, withPts: false),
            PesPacket(97 * Hz, streamId: 0xBE),
            PesPacket(2 * Hz));

        Assert.Equal(TimeSpan.FromSeconds(2), timeline.Last);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(187)]
    [InlineData(189)]
    [InlineData(500)]
    public void GivesTheSameResultWhateverTheChunkSize(int chunk)
    {
        var stream = new List<byte>();
        foreach (var pts in new long[] { 1 * Hz, 4 * Hz, 9 * Hz, 6 * Hz })
        {
            stream.AddRange(PesPacket(pts));
        }

        var timeline = new MpegTsTimeline();
        var bytes = stream.ToArray();
        for (int i = 0; i < bytes.Length; i += chunk)
        {
            timeline.Feed(bytes.AsSpan(i, Math.Min(chunk, bytes.Length - i)));
        }

        Assert.Equal(TimeSpan.FromSeconds(9), timeline.Last);
    }

    [Fact]
    public void StopsFollowingTheStreamWhenItLosesSync()
    {
        var timeline = new MpegTsTimeline();
        timeline.Feed(PesPacket(5 * Hz));
        timeline.Feed(Enumerable.Repeat((byte)0x00, 188).ToArray());
        timeline.Feed(PesPacket(50 * Hz));

        Assert.Equal(TimeSpan.FromSeconds(5), timeline.Last);
    }
}
