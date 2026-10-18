using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using Jellyfin.Plugin.JellyChannels.Models;

namespace Jellyfin.Plugin.JellyChannels.Services;

/// <summary>
/// Genera el EPG en formato XMLTV recorriendo la parrilla de cada canal.
/// </summary>
public static class EpgGenerator
{
    /// <summary>
    /// Genera el XMLTV.
    /// </summary>
    /// <param name="channels">Canales con su motor de parrilla.</param>
    /// <param name="urls">Constructor de URLs.</param>
    /// <param name="now">Instante desde el que se genera la guía.</param>
    /// <returns>Documento XMLTV.</returns>
    public static string Generate(IEnumerable<(Channel Channel, ScheduleEngine Schedule)> channels, GuideUrls urls, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(urls);

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = true,
            OmitXmlDeclaration = false
        };

        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, settings))
        {
            writer.WriteStartDocument();
            writer.WriteDocType("tv", null, "xmltv.dtd", null);
            writer.WriteStartElement("tv");
            writer.WriteAttributeString("generator-info-name", "JellyChannels");

            var list = new List<(Channel Channel, ScheduleEngine Schedule)>(channels);
            foreach (var (channel, _) in list)
            {
                WriteChannel(writer, channel, urls);
            }

            foreach (var (channel, schedule) in list)
            {
                var to = now.AddDays(channel.EpgDays);
                foreach (var entry in schedule.GetEntries(now, to))
                {
                    WriteProgramme(writer, channel, entry, urls);
                }
            }

            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Formatea una fecha al formato XMLTV (<c>yyyyMMddHHmmss +0000</c>).
    /// </summary>
    /// <param name="time">Fecha.</param>
    /// <returns>Texto.</returns>
    internal static string FormatTime(DateTimeOffset time)
        => time.UtcDateTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + " +0000";

    private static void WriteChannel(XmlWriter writer, Channel channel, GuideUrls urls)
    {
        writer.WriteStartElement("channel");
        writer.WriteAttributeString("id", GuideUrls.XmltvId(channel));
        writer.WriteElementString("display-name", channel.Name);
        writer.WriteElementString("display-name", channel.Number.ToString(CultureInfo.InvariantCulture));
        var logo = urls.Asset(channel.Logo);
        if (logo is not null)
        {
            writer.WriteStartElement("icon");
            writer.WriteAttributeString("src", logo);
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static void WriteProgramme(XmlWriter writer, Channel channel, ScheduleEntry entry, GuideUrls urls)
    {
        var item = entry.Item;
        writer.WriteStartElement("programme");
        writer.WriteAttributeString("start", FormatTime(entry.Start));
        writer.WriteAttributeString("stop", FormatTime(entry.End));
        writer.WriteAttributeString("channel", GuideUrls.XmltvId(channel));

        writer.WriteElementString("title", item.Title);
        if (!string.IsNullOrEmpty(item.EpisodeTitle))
        {
            writer.WriteElementString("sub-title", item.EpisodeTitle);
        }

        if (!string.IsNullOrEmpty(item.Overview))
        {
            writer.WriteElementString("desc", item.Overview);
        }

        if (item.Year is int year)
        {
            writer.WriteElementString("date", year.ToString(CultureInfo.InvariantCulture));
        }

        foreach (var genre in item.Genres)
        {
            writer.WriteElementString("category", genre);
        }

        writer.WriteStartElement("length");
        writer.WriteAttributeString("units", "minutes");
        writer.WriteString(((long)Math.Round(TimeSpan.FromTicks(item.DurationTicks).TotalMinutes)).ToString(CultureInfo.InvariantCulture));
        writer.WriteEndElement();

        if (item.ImageItemId is Guid imageId)
        {
            writer.WriteStartElement("icon");
            writer.WriteAttributeString("src", urls.ItemImage(imageId));
            writer.WriteEndElement();
        }

        if (item.Episode is int episode)
        {
            // xmltv_ns empieza en 0: "temporada.episodio.parte".
            var season = item.Season is int s ? (s - 1).ToString(CultureInfo.InvariantCulture) : string.Empty;
            writer.WriteStartElement("episode-num");
            writer.WriteAttributeString("system", "xmltv_ns");
            writer.WriteString(string.Create(CultureInfo.InvariantCulture, $"{season}.{episode - 1}."));
            writer.WriteEndElement();

            writer.WriteStartElement("episode-num");
            writer.WriteAttributeString("system", "onscreen");
            writer.WriteString(string.Create(CultureInfo.InvariantCulture, $"S{item.Season ?? 0:00}E{episode:00}"));
            writer.WriteEndElement();
        }

        if (!string.IsNullOrEmpty(item.OfficialRating))
        {
            writer.WriteStartElement("rating");
            writer.WriteElementString("value", item.OfficialRating);
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }
}
