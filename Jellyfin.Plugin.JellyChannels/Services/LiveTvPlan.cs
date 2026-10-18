using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Model.LiveTv;

namespace Jellyfin.Plugin.JellyChannels.Services;

/// <summary>
/// Decide qué hay que crear, actualizar o borrar en la configuración de Live TV de Jellyfin para que los
/// canales de JellyChannels aparezcan solos. Reconoce lo que es suyo por la ruta de la URL, así que adopta
/// el tuner y la guía que el usuario ya hubiera añadido a mano en vez de duplicarlos.
/// </summary>
public static class LiveTvPlan
{
    /// <summary>
    /// Nombre con el que aparece el tuner en Live TV.
    /// </summary>
    public const string TunerName = "JellyChannels";

    /// <summary>
    /// Ruta de la lista M3U del plugin.
    /// </summary>
    public const string M3uPath = "/JellyChannels/channels.m3u";

    /// <summary>
    /// Ruta del EPG del plugin.
    /// </summary>
    public const string EpgPath = "/JellyChannels/epg.xml";

    /// <summary>
    /// Indica si un tuner es el M3U de JellyChannels.
    /// </summary>
    /// <param name="tuner">El tuner.</param>
    /// <returns>True si lo es.</returns>
    public static bool IsOurTuner(TunerHostInfo tuner)
    {
        ArgumentNullException.ThrowIfNull(tuner);
        return string.Equals(tuner.Type, "m3u", StringComparison.OrdinalIgnoreCase)
            && tuner.Url?.Contains(M3uPath, StringComparison.OrdinalIgnoreCase) == true;
    }

    /// <summary>
    /// Indica si una guía es el XMLTV de JellyChannels.
    /// </summary>
    /// <param name="provider">La guía.</param>
    /// <returns>True si lo es.</returns>
    public static bool IsOurListings(ListingsProviderInfo provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return string.Equals(provider.Type, "xmltv", StringComparison.OrdinalIgnoreCase)
            && provider.Path?.Contains(EpgPath, StringComparison.OrdinalIgnoreCase) == true;
    }

    /// <summary>
    /// Calcula lo que hay que cambiar.
    /// </summary>
    /// <param name="tuners">Tuners que hay ahora en Live TV.</param>
    /// <param name="providers">Guías que hay ahora en Live TV.</param>
    /// <param name="m3uUrl">URL que debe tener el tuner.</param>
    /// <param name="epgUrl">URL que debe tener la guía.</param>
    /// <returns>El plan.</returns>
    public static LiveTvPlanResult Decide(IEnumerable<TunerHostInfo> tuners, IEnumerable<ListingsProviderInfo> providers, string m3uUrl, string epgUrl)
    {
        ArgumentNullException.ThrowIfNull(tuners);
        ArgumentNullException.ThrowIfNull(providers);

        var ours = tuners.Where(IsOurTuner).ToList();
        var oursListings = providers.Where(IsOurListings).ToList();

        var tuner = ours.FirstOrDefault();
        var listings = oursListings.FirstOrDefault();

        return new LiveTvPlanResult(
            tuner is null ? LiveTvAction.Create : (tuner.Url == m3uUrl ? LiveTvAction.None : LiveTvAction.Update),
            tuner,
            listings is null ? LiveTvAction.Create : (listings.Path == epgUrl ? LiveTvAction.None : LiveTvAction.Update),
            listings,
            ours.Skip(1).Select(t => t.Id).ToList(),
            oursListings.Skip(1).Select(p => p.Id).ToList());
    }
}
