using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using Jellyfin.Plugin.JellyChannels.Models;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyChannels.Services;

/// <summary>
/// Lectura y escritura de canales en <c>&lt;datos de Jellyfin&gt;/JellyChannels/channels.json</c>.
/// </summary>
public class ChannelStore
{
    private readonly ILogger<ChannelStore> _logger;
    private readonly string _filePath;
    private readonly Lock _lock = new();
    private List<Channel>? _channels;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChannelStore"/> class.
    /// </summary>
    /// <param name="applicationPaths">Rutas de Jellyfin.</param>
    /// <param name="logger">Logger.</param>
    public ChannelStore(IApplicationPaths applicationPaths, ILogger<ChannelStore> logger)
        : this(Path.Combine(applicationPaths?.DataPath ?? throw new ArgumentNullException(nameof(applicationPaths)), "JellyChannels"), logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ChannelStore"/> class.
    /// </summary>
    /// <param name="dataFolder">Carpeta de datos del plugin.</param>
    /// <param name="logger">Logger.</param>
    public ChannelStore(string dataFolder, ILogger<ChannelStore> logger)
    {
        DataFolder = dataFolder;
        _filePath = Path.Combine(dataFolder, "channels.json");
        _logger = logger;
    }

    /// <summary>
    /// Se lanza tras crear, editar o borrar un canal.
    /// </summary>
    public event EventHandler<ChannelChangedEventArgs>? Changed;

    /// <summary>
    /// Gets la carpeta de datos del plugin.
    /// </summary>
    public string DataFolder { get; }

    /// <summary>
    /// Gets la carpeta de logos y marcas de agua.
    /// </summary>
    public string AssetsFolder => Path.Combine(DataFolder, "assets");

    /// <summary>
    /// Devuelve todos los canales ordenados por número.
    /// </summary>
    /// <returns>Copias de los canales.</returns>
    public IReadOnlyList<Channel> GetAll()
    {
        lock (_lock)
        {
            return Load().OrderBy(c => c.Number).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Select(Clone).ToList();
        }
    }

    /// <summary>
    /// Devuelve un canal por id.
    /// </summary>
    /// <param name="id">Id del canal.</param>
    /// <returns>Copia del canal o null.</returns>
    public Channel? Get(string id)
    {
        lock (_lock)
        {
            var channel = Load().FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
            return channel is null ? null : Clone(channel);
        }
    }

    /// <summary>
    /// Crea un canal. Rellena id, semilla y fecha de inicio si faltan.
    /// </summary>
    /// <param name="channel">Canal nuevo.</param>
    /// <returns>El canal guardado.</returns>
    public Channel Create(Channel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        Channel saved;
        lock (_lock)
        {
            var channels = Load().ToList();
            saved = Clone(channel);
            saved.Id = UniqueId(channels, string.IsNullOrWhiteSpace(saved.Id) ? Slugify(saved.Name) : Slugify(saved.Id));
            Normalize(saved, channels);
            channels.Add(saved);
            Save(channels);
        }

        Changed?.Invoke(this, new ChannelChangedEventArgs(saved.Id));
        return Clone(saved);
    }

    /// <summary>
    /// Reemplaza un canal existente. Conserva semilla y fecha de inicio si no se indican.
    /// </summary>
    /// <param name="id">Id del canal.</param>
    /// <param name="channel">Datos nuevos.</param>
    /// <returns>El canal guardado, o null si no existe.</returns>
    public Channel? Update(string id, Channel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        Channel saved;
        lock (_lock)
        {
            var channels = Load().ToList();
            int index = channels.FindIndex(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return null;
            }

            var existing = channels[index];
            saved = Clone(channel);
            saved.Id = existing.Id;
            saved.Seed ??= existing.Seed;
            saved.StartDate ??= existing.StartDate;
            channels.RemoveAt(index);
            Normalize(saved, channels);
            channels.Insert(index, saved);
            Save(channels);
        }

        Changed?.Invoke(this, new ChannelChangedEventArgs(saved.Id));
        return Clone(saved);
    }

    /// <summary>
    /// Borra un canal.
    /// </summary>
    /// <param name="id">Id del canal.</param>
    /// <returns>True si existía.</returns>
    public bool Delete(string id)
    {
        lock (_lock)
        {
            var channels = Load().ToList();
            if (channels.RemoveAll(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)) == 0)
            {
                return false;
            }

            Save(channels);
        }

        Changed?.Invoke(this, new ChannelChangedEventArgs(id));
        return true;
    }

    /// <summary>
    /// Convierte un texto en un id válido (minúsculas, dígitos y guiones).
    /// </summary>
    /// <param name="value">Texto.</param>
    /// <returns>Slug.</returns>
    internal static string Slugify(string? value)
    {
        var builder = new StringBuilder();
        foreach (char c in (value ?? string.Empty).Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString().Trim('-');
        return slug.Length == 0 ? "canal" : slug;
    }

    private static string UniqueId(List<Channel> channels, string baseId)
    {
        var id = baseId;
        for (int i = 2; channels.Any(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)); i++)
        {
            id = string.Create(CultureInfo.InvariantCulture, $"{baseId}-{i}");
        }

        return id;
    }

    private static void Normalize(Channel channel, List<Channel> others)
    {
        channel.Name = string.IsNullOrWhiteSpace(channel.Name) ? channel.Id : channel.Name.Trim();
        if (channel.Number <= 0)
        {
            channel.Number = others.Count == 0 ? 1 : others.Max(c => c.Number) + 1;
        }

        channel.EpgDays = Math.Clamp(channel.EpgDays, 1, 14);
        channel.Seed = channel.Seed is long seed ? StableRandom.ClampSeed(seed) : StableRandom.DefaultSeed(channel.Id);
        if (channel.StartDate is null)
        {
            var now = DateTimeOffset.UtcNow;
            channel.StartDate = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, TimeSpan.Zero);
        }
    }

    private static Channel Clone(Channel channel)
        => JsonSerializer.Deserialize<Channel>(JsonSerializer.Serialize(channel, ChannelJson.Options), ChannelJson.Options)!;

    private List<Channel> Load()
    {
        if (_channels is not null)
        {
            return _channels;
        }

        if (!File.Exists(_filePath))
        {
            _channels = [];
            return _channels;
        }

        try
        {
            using var stream = File.OpenRead(_filePath);
            _channels = JsonSerializer.Deserialize<List<Channel>>(stream, ChannelJson.Options) ?? [];
        }
        catch (JsonException ex)
        {
            var backupPath = _filePath + ".corrupt";
            File.Copy(_filePath, backupPath, overwrite: true);
            _logger.LogError(ex, "No se pudo leer {Path}; copia guardada en {Backup}, se empieza sin canales", _filePath, backupPath);
            _channels = [];
        }

        return _channels;
    }

    private void Save(List<Channel> channels)
    {
        Directory.CreateDirectory(DataFolder);
        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(channels, ChannelJson.Options));
        File.Move(tempPath, _filePath, overwrite: true);
        _channels = channels;
    }
}
