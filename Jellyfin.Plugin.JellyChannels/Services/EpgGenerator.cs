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

    /// <summary>
    /// Quita los caracteres que XML 1.0 no admite. Un solo título o sinopsis con un carácter de
    /// control haría fallar el documento entero.
    /// </summary>
    /// <param name="text">Texto.</param>
    /// <returns>Texto válido en XML.</returns>
    internal static string Clean(string text)
    {
        StringBuilder? builder = null;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            bool valid = XmlConvert.IsXmlChar(c);
            int length = 1;
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && XmlConvert.IsXmlSurrogatePair(text[i + 1], c))
            {
                valid = true;
                length = 2;
            }

            if (valid)
            {
                builder?.Append(text, i, length);
            }
            else
            {
                builder ??= new StringBuilder(text.Length).Append(text, 0, i);
            }

            i += length - 1;
        }

        return builder?.ToString() ?? text;
    }

    private static void WriteChannel(XmlWriter writer, Channel channel, GuideUrls urls)
    {
        writer.WriteStartElement("channel");
        writer.WriteAttributeString("id", GuideUrls.XmltvId(channel));
        writer.WriteElementString("display-name", Clean(channel.Name));
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

        writer.WriteElementString("title", Clean(item.Title));
        if (!string.IsNullOrEmpty(item.EpisodeTitle))
        {
            writer.WriteElementString("sub-title", Clean(item.EpisodeTitle));
        }

        if (!string.IsNullOrEmpty(item.Overview))
        {
            writer.WriteElementString("desc", Clean(item.Overview));
        }

        if (item.Year is int year)
        {
            writer.WriteElementString("date", year.ToString(CultureInfo.InvariantCulture));
        }

        foreach (var genre in item.Genres)
        {
            writer.WriteElementString("category", Clean(genre));
        }

        writer.WriteStartElement("length");
        writer.WriteAttributeString("units", "minutes");
        writer.WriteString(Math.Max(1, (long)Math.Round(TimeSpan.FromTicks(item.DurationTicks).TotalMinutes, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture));
        writer.WriteEndElement();

        var image = item.ImageUrl ?? (item.ImageItemId is Guid imageId ? urls.ItemImage(imageId) : null);
        if (image is not null)
        {
            writer.WriteStartElement("icon");
            writer.WriteAttributeString("src", image);
            writer.WriteEndElement();
        }

        if (item.Episode is int episode)
        {
            // xmltv_ns empieza en 0: "temporada.episodio.parte". Los especiales (temporada 0) y el
            // episodio 0 no tienen posición válida, así que ese campo se deja vacío.
            if (episode >= 1)
            {
                var season = item.Season is >= 1 ? (item.Season.Value - 1).ToString(CultureInfo.InvariantCulture) : string.Empty;
                writer.WriteStartElement("episode-num");
                writer.WriteAttributeString("system", "xmltv_ns");
                writer.WriteString(string.Create(CultureInfo.InvariantCulture, $"{season}.{episode - 1}."));
                writer.WriteEndElement();
            }

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
