using System.Collections.Generic;
using MediaBrowser.Model.LiveTv;

namespace Jellyfin.Plugin.JellyChannels.Services;

/// <summary>
/// Lo que hay que hacer en Live TV para que JellyChannels esté registrado.
/// </summary>
/// <param name="Tuner">Qué hacer con el tuner M3U.</param>
/// <param name="ExistingTuner">El tuner que ya existe, si lo hay.</param>
/// <param name="Listings">Qué hacer con la guía XMLTV.</param>
/// <param name="ExistingListings">La guía que ya existe, si la hay.</param>
/// <param name="DuplicateTunerIds">Tuners repetidos de JellyChannels, que duplicarían los canales.</param>
/// <param name="DuplicateListingIds">Guías repetidas de JellyChannels.</param>
public sealed record LiveTvPlanResult(
    LiveTvAction Tuner,
    TunerHostInfo? ExistingTuner,
    LiveTvAction Listings,
    ListingsProviderInfo? ExistingListings,
    IReadOnlyList<string> DuplicateTunerIds,
    IReadOnlyList<string> DuplicateListingIds)
{
    /// <summary>
    /// Gets a value indicating whether hay algo que cambiar en Live TV.
    /// </summary>
    public bool HasChanges => Tuner != LiveTvAction.None || Listings != LiveTvAction.None || DuplicateTunerIds.Count > 0 || DuplicateListingIds.Count > 0;
}
