using System;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.JellyChannels.Models;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyChannels.Services;

/// <summary>
/// Resuelve items de la biblioteca de Jellyfin a un fichero y sus pistas.
/// </summary>
public class PlaybackSourceResolver : IPlaybackSourceResolver
{
    private readonly IMediaSourceManager _mediaSourceManager;
    private readonly YoutubeCache _youtube;
    private readonly ILogger<PlaybackSourceResolver> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackSourceResolver"/> class.
    /// </summary>
    /// <param name="mediaSourceManager">Gestor de fuentes de medios de Jellyfin.</param>
    /// <param name="youtube">Caché de vídeos de YouTube.</param>
    /// <param name="logger">Logger.</param>
    public PlaybackSourceResolver(IMediaSourceManager mediaSourceManager, YoutubeCache youtube, ILogger<PlaybackSourceResolver> logger)
    {
        _mediaSourceManager = mediaSourceManager;
        _youtube = youtube;
        _logger = logger;
    }

    /// <inheritdoc />
    public PlaybackSource? Resolve(ScheduleItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.YoutubeId is not null)
        {
            return _youtube.GetPlaybackSource(item.YoutubeId);
        }

        if (string.IsNullOrEmpty(item.Path))
        {
            return null;
        }

        try
        {
            var streams = _mediaSourceManager.GetMediaStreams(item.ItemId);
            if (streams.Count == 0)
            {
                return new PlaybackSource(item.Path, null, null, StreamsKnown: false);
            }

            var video = streams.FirstOrDefault(s => s.Type == MediaStreamType.Video);
            var audio = streams.FirstOrDefault(s => s.Type == MediaStreamType.Audio);
            return new PlaybackSource(
                item.Path,
                video is null ? null : new StreamInfo(video.Codec?.ToLowerInvariant(), video.BitDepth, PixelFormat: video.PixelFormat),
                audio is null ? null : new StreamInfo(audio.Codec?.ToLowerInvariant(), Channels: audio.Channels));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            _logger.LogWarning(ex, "No se pudieron leer las pistas de «{Title}»", item.Title);
            return new PlaybackSource(item.Path, null, null, StreamsKnown: false);
        }
    }
}
