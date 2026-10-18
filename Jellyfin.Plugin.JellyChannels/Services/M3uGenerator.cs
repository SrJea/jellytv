using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Jellyfin.Plugin.JellyChannels.Models;

namespace Jellyfin.Plugin.JellyChannels.Services;

/// <summary>
/// Genera la lista <c>channels.m3u</c>.
/// </summary>
public static class M3uGenerator
{
    /// <summary>
    /// Genera el M3U.
    /// </summary>
    /// <param name="channels">Canales.</param>
    /// <param name="urls">Constructor de URLs.</param>
    /// <returns>Contenido del M3U.</returns>
    public static string Generate(IEnumerable<Channel> channels, GuideUrls urls)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(urls);

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"#EXTM3U url-tvg=\"{urls.Epg()}\"\n");
        foreach (var channel in channels)
        {
            sb.Append(CultureInfo.InvariantCulture, $"#EXTINF:-1 tvg-id=\"{Attr(GuideUrls.XmltvId(channel))}\" tvg-name=\"{Attr(channel.Name)}\" tvg-chno=\"{channel.Number}\"");
            var logo = urls.Asset(channel.Logo);
            if (logo is not null)
            {
                sb.Append(CultureInfo.InvariantCulture, $" tvg-logo=\"{Attr(logo)}\"");
            }

            sb.Append(CultureInfo.InvariantCulture, $" group-title=\"JellyChannels\",{Line(channel.Name)}\n");
            sb.Append(urls.Stream(channel)).Append('\n');
        }

        return sb.ToString();
    }

    private static string Attr(string value) => Line(value).Replace("\"", "'", StringComparison.Ordinal);

    private static string Line(string value) => value.Replace('\r', ' ').Replace('\n', ' ');
}
