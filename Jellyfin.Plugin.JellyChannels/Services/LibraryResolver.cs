using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.JellyChannels.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using SourceType = Jellyfin.Plugin.JellyChannels.Models.SourceType;

namespace Jellyfin.Plugin.JellyChannels.Services;

/// <summary>
/// Resuelve fuentes (serie, película, colección, tag) a items vía <see cref="ILibraryManager"/>.
/// </summary>
public class LibraryResolver : ILibraryResolver
{
    private static readonly BaseItemKind[] _playableKinds =
    [
        BaseItemKind.Movie,
        BaseItemKind.Episode,
        BaseItemKind.MusicVideo,
        BaseItemKind.Video
    ];

    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<LibraryResolver> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryResolver"/> class.
    /// </summary>
    /// <param name="libraryManager">Gestor de biblioteca.</param>
    /// <param name="logger">Logger.</param>
    public LibraryResolver(ILibraryManager libraryManager, ILogger<LibraryResolver> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<ScheduleItem> Resolve(Channel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        var seen = new HashSet<Guid>();
        var result = new List<ScheduleItem>();
        foreach (var source in channel.Sources)
        {
            foreach (var item in ResolveSource(channel, source))
            {
                if (item.RunTimeTicks is > 0 && seen.Add(item.Id))
                {
                    result.Add(Map(item));
                }
            }
        }

        return result;
    }

    private static ScheduleItem Map(BaseItem item)
    {
        var episode = item as Episode;
        Guid? imageItemId = item.HasImage(ImageType.Primary, 0) ? item.Id : null;
        if (imageItemId is null && episode is not null && episode.SeriesId != Guid.Empty)
        {
            imageItemId = episode.SeriesId;
        }

        return new ScheduleItem
        {
            ItemId = item.Id,
            DurationTicks = item.RunTimeTicks ?? 0,
            Title = episode is null ? item.Name : (episode.SeriesName ?? item.Name),
            EpisodeTitle = episode is null ? null : item.Name,
            Overview = item.Overview,
            Season = episode?.ParentIndexNumber,
            Episode = episode?.IndexNumber,
            Year = item.ProductionYear,
            OfficialRating = item.OfficialRating,
            Genres = item.Genres ?? [],
            Kind = item.GetBaseItemKind().ToString(),
            ImageItemId = imageItemId
        };
    }

    private static int SeasonSortKey(BaseItem episode)
        => episode.ParentIndexNumber switch
        {
            null => int.MaxValue,
            0 => int.MaxValue - 1, // especiales al final
            var n => n.Value
        };

    private IEnumerable<BaseItem> ResolveSource(Channel channel, ChannelSource source)
    {
        switch (source.Type)
        {
            case SourceType.Series:
            case SourceType.Movie:
            case SourceType.Collection:
                if (!Guid.TryParse(source.Id, out var id))
                {
                    _logger.LogWarning("Canal {Channel}: fuente {Type} con id no válido '{Id}'", channel.Id, source.Type, source.Id);
                    return [];
                }

                var item = _libraryManager.GetItemById(id);
                if (item is null)
                {
                    _logger.LogWarning("Canal {Channel}: no existe el item {Id}", channel.Id, id);
                    return [];
                }

                return Expand(item, 0);

            case SourceType.Tag:
                if (string.IsNullOrWhiteSpace(source.Value))
                {
                    return [];
                }

                var tagged = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    Tags = [source.Value],
                    IncludeItemTypes = [.. _playableKinds, BaseItemKind.Series, BaseItemKind.BoxSet],
                    Recursive = true,
                    IsVirtualItem = false
                });
                return tagged
                    .OrderBy(i => i.SortName, StringComparer.OrdinalIgnoreCase)
                    .SelectMany(i => Expand(i, 0));

            case SourceType.Youtube:
                _logger.LogDebug("Canal {Channel}: las fuentes de YouTube aún no están implementadas (v0.6)", channel.Id);
                return [];

            default:
                return [];
        }
    }

    private IEnumerable<BaseItem> Expand(BaseItem item, int depth)
    {
        if (depth > 3)
        {
            return [];
        }

        switch (item)
        {
            case Series or Season:
                var episodes = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    AncestorIds = [item.Id],
                    IncludeItemTypes = [BaseItemKind.Episode],
                    Recursive = true,
                    IsVirtualItem = false
                });
                return episodes
                    .OrderBy(SeasonSortKey)
                    .ThenBy(e => e.IndexNumber ?? int.MaxValue)
                    .ThenBy(e => e.SortName, StringComparer.OrdinalIgnoreCase);

            case Folder folder:
                return folder.GetLinkedChildren().SelectMany(child => Expand(child, depth + 1));

            default:
                return item.IsFolder || item.IsVirtualItem ? [] : [item];
        }
    }
}
