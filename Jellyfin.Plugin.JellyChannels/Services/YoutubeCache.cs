using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyChannels.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyChannels.Services;

/// <summary>
/// Metadatos, descarga a demanda y borrado de los vídeos de YouTube.
/// Los vídeos no se descargan al añadirlos: solo se leen sus metadatos, que bastan para meterlos en la
/// parrilla. El fichero se baja poco antes de que toque emitirlo y se borra según su política de retención.
/// </summary>
public sealed class YoutubeCache : IYoutubeCatalog, IDisposable
{
    private const int MaxDescriptionChars = 2000;
    private const int MaxBackoffMinutes = 6 * 60;
    private static readonly TimeSpan _metadataTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan _downloadTimeout = TimeSpan.FromHours(2);

    // LastNeededAt solo se usa con granularidad de días (keep-days) y para ordenar el borrado por límite,
    // así que no hace falta reescribir el estado cada minuto mientras un vídeo está en su ventana.
    private static readonly TimeSpan _neededResolution = TimeSpan.FromMinutes(5);

    private readonly string _stateFile;
    private readonly IYtDlpRunner _runner;
    private readonly ILogger<YoutubeCache> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly Func<YoutubeSettings> _settings;
    private readonly Lock _lock = new();
    private readonly SemaphoreSlim _downloadGate = new(1, 1);
    private readonly HashSet<string> _needed = new(StringComparer.Ordinal);
    private Dictionary<string, YoutubeVideo>? _videos;
    private string? _lastSavedJson;

    /// <summary>
    /// Initializes a new instance of the <see cref="YoutubeCache"/> class.
    /// </summary>
    /// <param name="store">Almacén de canales, que da la carpeta de datos del plugin.</param>
    /// <param name="runner">Ejecutor de yt-dlp.</param>
    /// <param name="logger">Logger.</param>
    public YoutubeCache(ChannelStore store, IYtDlpRunner runner, ILogger<YoutubeCache> logger)
        : this(store?.DataFolder ?? throw new ArgumentNullException(nameof(store)), runner, logger, TimeProvider.System, YoutubeSettings.FromConfiguration)
    {
    }

    internal YoutubeCache(string dataFolder, IYtDlpRunner runner, ILogger<YoutubeCache> logger, TimeProvider timeProvider, Func<YoutubeSettings> settings)
    {
        DataFolder = dataFolder;
        CacheFolder = Path.Combine(dataFolder, "cache", "youtube");
        _stateFile = Path.Combine(dataFolder, "youtube.json");
        _runner = runner;
        _logger = logger;
        _timeProvider = timeProvider;
        _settings = settings;
    }

    /// <summary>
    /// Se lanza cuando cambia algo que afecta a la parrilla o al estado visible (metadatos nuevos, descarga o borrado).
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets la carpeta de datos del plugin.
    /// </summary>
    public string DataFolder { get; }

    /// <summary>
    /// Gets la carpeta donde se guardan los vídeos descargados.
    /// </summary>
    public string CacheFolder { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        _downloadGate.Dispose();
    }

    /// <summary>
    /// Devuelve todos los vídeos conocidos.
    /// </summary>
    /// <returns>Copias de los registros, por título.</returns>
    public IReadOnlyList<YoutubeVideo> GetAll()
    {
        lock (_lock)
        {
            return Load().Values.OrderBy(v => v.Title ?? v.Id, StringComparer.OrdinalIgnoreCase).Select(Clone).ToList();
        }
    }

    /// <summary>
    /// Devuelve un vídeo.
    /// </summary>
    /// <param name="videoId">Id de YouTube.</param>
    /// <returns>Copia del registro, o null.</returns>
    public YoutubeVideo? Get(string videoId)
    {
        lock (_lock)
        {
            return Load().TryGetValue(videoId, out var video) ? Clone(video) : null;
        }
    }

    /// <inheritdoc />
    public ScheduleItem? GetScheduleItem(string videoId)
    {
        if (!_settings().Enabled)
        {
            return null;
        }

        var video = Get(videoId);
        if (video?.Title is null || video.DurationSeconds <= 0)
        {
            return null;
        }

        return new ScheduleItem
        {
            ItemId = YoutubeId.ToGuid(video.Id),
            DurationTicks = (long)(video.DurationSeconds * TimeSpan.TicksPerSecond),
            Title = video.Title,
            Overview = video.Description,
            Genres = ["YouTube"],
            Kind = YoutubeId.Kind,
            YoutubeId = video.Id,
            ImageUrl = video.Thumbnail
        };
    }

    /// <summary>
    /// Devuelve la ruta del vídeo descargado, o null si no está en la caché.
    /// </summary>
    /// <param name="videoId">Id de YouTube.</param>
    /// <returns>Ruta absoluta.</returns>
    public string? GetFilePath(string videoId)
    {
        bool stale = false;
        string? path = null;
        lock (_lock)
        {
            if (Load().TryGetValue(videoId, out var video) && video.File is not null)
            {
                path = SafePath(video.File);
                if (path is null || !System.IO.File.Exists(path))
                {
                    // Lo borraron por fuera: se olvida el fichero para que se vuelva a descargar.
                    video.File = null;
                    video.SizeBytes = 0;
                    video.DownloadedAt = null;
                    path = null;
                    stale = true;
                    Save();
                }
            }
        }

        if (stale)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return path;
    }

    /// <summary>
    /// Describe cómo reproducir un vídeo descargado: el fichero y los códecs que anunció yt-dlp.
    /// </summary>
    /// <param name="videoId">Id de YouTube.</param>
    /// <returns>El fichero y sus pistas, o null si aún no está descargado.</returns>
    public PlaybackSource? GetPlaybackSource(string videoId)
    {
        var path = GetFilePath(videoId);
        var video = path is null ? null : Get(videoId);
        if (path is null || video is null)
        {
            return null;
        }

        var videoCodec = NormalizeVideoCodec(video.VideoCodec);
        var audioCodec = NormalizeAudioCodec(video.AudioCodec);
        if (videoCodec is null && audioCodec is null)
        {
            return new PlaybackSource(path, null, null, StreamsKnown: false);
        }

        // YouTube sirve siempre 4:2:0, así que no se informa del formato de píxel.
        return new PlaybackSource(
            path,
            videoCodec is null ? null : new StreamInfo(videoCodec),
            audioCodec is null ? null : new StreamInfo(audioCodec));
    }

    /// <summary>
    /// Lee los metadatos de un vídeo con yt-dlp, sin descargarlo.
    /// </summary>
    /// <param name="videoId">Id de YouTube.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>True si ahora el vídeo tiene metadatos.</returns>
    public async Task<bool> FetchMetadataAsync(string videoId, CancellationToken cancellationToken)
    {
        var settings = _settings();
        string[] args =
        [
            "--dump-single-json", "--no-playlist", "--no-warnings", "--skip-download",
            "--socket-timeout", "30", "-f", settings.Format, "--", YoutubeId.CanonicalUrl(videoId)
        ];

        var result = await _runner.RunAsync(args, _metadataTimeout, cancellationToken).ConfigureAwait(false);
        string? error = null;
        if (!result.Succeeded)
        {
            error = result.LastErrorLine();
        }
        else if (!TryApplyMetadata(videoId, result.StdOut, out error))
        {
            _logger.LogWarning("Metadatos de YouTube {Id} no válidos: {Error}", videoId, error);
        }

        if (error is null)
        {
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        RecordFailure(videoId, error);
        return false;
    }

    /// <summary>
    /// Descarga un vídeo a la caché. Una sola descarga a la vez.
    /// </summary>
    /// <param name="videoId">Id de YouTube.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>True si el vídeo quedó en la caché.</returns>
    public async Task<bool> DownloadAsync(string videoId, CancellationToken cancellationToken)
    {
        await _downloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var settings = _settings();
            lock (_lock)
            {
                if (!Load().TryGetValue(videoId, out var video) || video.Title is null)
                {
                    return false;
                }

                if (video.File is not null)
                {
                    return true;
                }

                if (!EnforceLimit(settings, _timeProvider.GetUtcNow(), null))
                {
                    video.LastError = "La caché de YouTube está llena y no hay nada que se pueda borrar.";
                    Save();
                    _logger.LogWarning("No se descarga {Id}: {Error}", videoId, video.LastError);
                    return false;
                }
            }

            Directory.CreateDirectory(CacheFolder);
            string[] args =
            [
                "-f", settings.Format,
                "-o", Path.Combine(CacheFolder, "%(id)s.%(ext)s"),
                "--no-playlist", "--no-mtime", "--no-warnings", "--newline",
                "--retries", "3", "--socket-timeout", "30",
                "--print", "after_move:filepath",
                "--", YoutubeId.CanonicalUrl(videoId)
            ];

            _logger.LogInformation("Descargando el vídeo de YouTube {Id}", videoId);
            var result = await _runner.RunAsync(args, _downloadTimeout, cancellationToken).ConfigureAwait(false);
            var file = result.Succeeded ? FindDownloadedFile(result.StdOut, videoId) : null;
            if (file is null)
            {
                RecordFailure(videoId, result.Succeeded ? "yt-dlp terminó pero no dejó el fichero en la caché" : result.LastErrorLine());
                DeletePartialFiles(videoId);
                return false;
            }

            lock (_lock)
            {
                var video = Load()[videoId];
                video.File = Path.GetFileName(file);
                video.SizeBytes = new FileInfo(file).Length;
                video.DownloadedAt = _timeProvider.GetUtcNow();
                video.Failures = 0;
                video.NextAttemptAt = null;
                video.LastError = null;
                Save();
            }

            _logger.LogInformation("Vídeo de YouTube {Id} en la caché", videoId);
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }
        finally
        {
            _downloadGate.Release();
        }
    }

    /// <summary>
    /// Repasa la caché: apunta qué vídeos usa cada canal y cuáles hacen falta pronto, borra lo que
    /// ya no hace falta según la retención y el límite de disco, y devuelve lo que queda por hacer.
    /// </summary>
    /// <param name="references">Todos los vídeos que usa alguna fuente.</param>
    /// <param name="needs">Huecos de la parrilla que caen dentro de la ventana de descarga.</param>
    /// <param name="now">Instante actual.</param>
    /// <returns>Qué leer, qué descargar y qué se borró.</returns>
    public YoutubePlan Maintain(IEnumerable<YoutubeReference> references, IEnumerable<YoutubeNeed> needs, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(needs);

        var settings = _settings();
        var metadata = new List<string>();
        var download = new List<string>();
        var deleted = new List<string>();

        lock (_lock)
        {
            var videos = Load();
            var referenced = new Dictionary<string, YoutubeRetention>(StringComparer.Ordinal);
            foreach (var reference in references)
            {
                referenced[reference.VideoId] = referenced.TryGetValue(reference.VideoId, out var existing)
                    ? YoutubeRetention.MostConservative(existing, reference.Retention)
                    : reference.Retention;
            }

            foreach (var (id, retention) in referenced)
            {
                if (!videos.TryGetValue(id, out var video))
                {
                    videos[id] = video = new YoutubeVideo { Id = id };
                }

                video.Retention = retention.ToText();
            }

            _needed.Clear();
            foreach (var need in needs)
            {
                _needed.Add(need.VideoId);
                if (videos.TryGetValue(need.VideoId, out var video) && (video.LastNeededAt is null || now - video.LastNeededAt >= _neededResolution))
                {
                    video.LastNeededAt = now;
                }
            }

            if (settings.Enabled)
            {
                foreach (var id in referenced.Keys)
                {
                    var video = videos[id];
                    if (video.Title is null && CanAttempt(video, now))
                    {
                        metadata.Add(id);
                    }
                }

                foreach (var id in _needed)
                {
                    if (videos.TryGetValue(id, out var video) && video.Title is not null && video.File is null && CanAttempt(video, now))
                    {
                        download.Add(id);
                    }
                }
            }

            foreach (var video in videos.Values.Where(v => v.File is not null).ToList())
            {
                if (!_needed.Contains(video.Id) && ShouldDeleteByRetention(video, now))
                {
                    DeleteFileLocked(video);
                    deleted.Add(video.Id);
                }
            }

            EnforceLimit(settings, now, deleted);

            // Lo que no usa ningún canal ni ocupa disco no hace falta recordarlo.
            foreach (var id in videos.Keys.Where(id => !referenced.ContainsKey(id) && videos[id].File is null).ToList())
            {
                videos.Remove(id);
            }

            Save();
        }

        if (deleted.Count > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return new YoutubePlan(metadata, download, deleted);
    }

    /// <summary>
    /// Borra el fichero descargado de un vídeo (borrado manual). Los metadatos se conservan.
    /// </summary>
    /// <param name="videoId">Id de YouTube.</param>
    /// <returns>True si había un fichero.</returns>
    public bool DeleteFile(string videoId)
    {
        lock (_lock)
        {
            if (!Load().TryGetValue(videoId, out var video) || video.File is null)
            {
                return false;
            }

            DeleteFileLocked(video);
            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Olvida los fallos de un vídeo para que se reintente ya.
    /// </summary>
    /// <param name="videoId">Id de YouTube.</param>
    /// <returns>True si existe.</returns>
    public bool ResetFailures(string videoId)
    {
        lock (_lock)
        {
            if (!Load().TryGetValue(videoId, out var video))
            {
                return false;
            }

            video.Failures = 0;
            video.NextAttemptAt = null;
            video.LastError = null;
            Save();
            return true;
        }
    }

    /// <summary>
    /// Convierte el códec de vídeo que anuncia yt-dlp (<c>avc1.64001F</c>, <c>vp09.00.40.08</c>...) al nombre que usa ffmpeg.
    /// </summary>
    /// <param name="codec">Códec de yt-dlp.</param>
    /// <returns>Nombre normalizado, o null si no se conoce.</returns>
    internal static string? NormalizeVideoCodec(string? codec)
    {
        var name = codec?.Trim().ToLowerInvariant();
        return name switch
        {
            null or "" or "none" => null,
            _ when name.StartsWith("avc", StringComparison.Ordinal) || name == "h264" => "h264",
            _ when name.StartsWith("vp09", StringComparison.Ordinal) || name == "vp9" => "vp9",
            _ when name.StartsWith("av01", StringComparison.Ordinal) || name == "av1" => "av1",
            _ when name.StartsWith("hev1", StringComparison.Ordinal) || name.StartsWith("hvc1", StringComparison.Ordinal) || name == "hevc" => "hevc",
            _ => name.Split('.')[0]
        };
    }

    /// <summary>
    /// Convierte el códec de audio que anuncia yt-dlp (<c>mp4a.40.2</c>, <c>opus</c>...) al nombre que usa ffmpeg.
    /// </summary>
    /// <param name="codec">Códec de yt-dlp.</param>
    /// <returns>Nombre normalizado, o null si no se conoce.</returns>
    internal static string? NormalizeAudioCodec(string? codec)
    {
        var name = codec?.Trim().ToLowerInvariant();
        return name switch
        {
            null or "" or "none" => null,
            _ when name.StartsWith("mp4a", StringComparison.Ordinal) || name == "aac" => "aac",
            "ac-3" => "ac3",
            "ec-3" => "eac3",
            _ => name.Split('.')[0]
        };
    }

    private static YoutubeVideo Clone(YoutubeVideo video)
        => JsonSerializer.Deserialize<YoutubeVideo>(JsonSerializer.Serialize(video, ChannelJson.Options), ChannelJson.Options)!;

    private static bool CanAttempt(YoutubeVideo video, DateTimeOffset now) => video.NextAttemptAt is null || video.NextAttemptAt <= now;

    private static string? TruncateText(string? text, int max)
        => string.IsNullOrWhiteSpace(text) ? null : (text.Length <= max ? text.Trim() : text[..max].TrimEnd() + "…");

    private bool TryApplyMetadata(string videoId, string json, out string? error)
    {
        error = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "La respuesta de yt-dlp no es un objeto JSON.";
                return false;
            }

            string? Text(string name) => root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

            if ((root.TryGetProperty("is_live", out var live) && live.ValueKind == JsonValueKind.True)
                || Text("live_status") is "is_live" or "is_upcoming" or "post_live")
            {
                error = "Es una emisión en directo o aún no empezó; solo se admiten vídeos.";
                return false;
            }

            if (!root.TryGetProperty("duration", out var duration) || duration.ValueKind != JsonValueKind.Number || duration.GetDouble() <= 0)
            {
                error = "yt-dlp no informó de la duración del vídeo.";
                return false;
            }

            var thumbnail = Text("thumbnail");
            if (thumbnail is not null && !(Uri.TryCreate(thumbnail, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)))
            {
                thumbnail = null;
            }

            lock (_lock)
            {
                var videos = Load();
                if (!videos.TryGetValue(videoId, out var video))
                {
                    videos[videoId] = video = new YoutubeVideo { Id = videoId };
                }

                video.Title = TruncateText(Text("title"), 300) ?? videoId;
                video.DurationSeconds = duration.GetDouble();
                video.Thumbnail = thumbnail;
                video.Description = TruncateText(Text("description"), MaxDescriptionChars);
                video.Uploader = Text("channel") ?? Text("uploader");
                video.VideoCodec = Text("vcodec");
                video.AudioCodec = Text("acodec");
                video.Ext = Text("ext");
                video.MetadataAt = _timeProvider.GetUtcNow();
                video.Failures = 0;
                video.NextAttemptAt = null;
                video.LastError = null;
                Save();
            }

            return true;
        }
        catch (JsonException ex)
        {
            error = "La respuesta de yt-dlp no es JSON válido: " + ex.Message;
            return false;
        }
    }

    private void RecordFailure(string videoId, string error)
    {
        lock (_lock)
        {
            var videos = Load();
            if (!videos.TryGetValue(videoId, out var video))
            {
                videos[videoId] = video = new YoutubeVideo { Id = videoId };
            }

            video.Failures++;
            video.LastError = TruncateText(error, 500);
            video.NextAttemptAt = _timeProvider.GetUtcNow().AddMinutes(Math.Min(5 * Math.Pow(2, Math.Min(video.Failures - 1, 10)), MaxBackoffMinutes));
            Save();
            _logger.LogWarning("YouTube {Id}: {Error} (fallo {Count}; se reintentará {Next:u})", videoId, video.LastError, video.Failures, video.NextAttemptAt);
        }
    }

    private bool ShouldDeleteByRetention(YoutubeVideo video, DateTimeOffset now)
    {
        if (!YoutubeRetention.TryParse(video.Retention, out var retention))
        {
            retention = YoutubeRetention.Default;
        }

        return retention.Kind switch
        {
            YoutubeRetentionKind.Keep => false,
            YoutubeRetentionKind.KeepDays => now - (video.LastNeededAt ?? video.DownloadedAt ?? now) >= TimeSpan.FromDays(retention.Days),
            _ => true
        };
    }

    /// <summary>
    /// Borra, si hace falta, los ya emitidos más antiguos hasta bajar del límite. Los que se conservan para
    /// siempre y los que hacen falta pronto no se tocan.
    /// </summary>
    /// <returns>False si sigue por encima del límite.</returns>
    private bool EnforceLimit(YoutubeSettings settings, DateTimeOffset now, List<string>? deleted)
    {
        if (settings.MaxCacheBytes <= 0)
        {
            return true;
        }

        var videos = Load();
        long total = videos.Values.Where(v => v.File is not null).Sum(v => v.SizeBytes);
        if (total < settings.MaxCacheBytes)
        {
            return true;
        }

        var candidates = videos.Values
            .Where(v => v.File is not null && !_needed.Contains(v.Id) && !(YoutubeRetention.TryParse(v.Retention, out var r) && r.Kind == YoutubeRetentionKind.Keep))
            .OrderBy(v => v.LastNeededAt ?? v.DownloadedAt ?? now)
            .ToList();
        foreach (var video in candidates)
        {
            if (total < settings.MaxCacheBytes)
            {
                break;
            }

            total -= video.SizeBytes;
            _logger.LogInformation("Caché de YouTube por encima del límite: se borra {Id}", video.Id);
            DeleteFileLocked(video);
            deleted?.Add(video.Id);
        }

        return total < settings.MaxCacheBytes;
    }

    private void DeleteFileLocked(YoutubeVideo video)
    {
        var path = video.File is null ? null : SafePath(video.File);
        if (path is not null)
        {
            try
            {
                System.IO.File.Delete(path);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "No se pudo borrar {Path}", path);
            }
        }

        video.File = null;
        video.SizeBytes = 0;
        video.DownloadedAt = null;
    }

    private void DeletePartialFiles(string videoId)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(CacheFolder, videoId + ".*"))
            {
                if (file.EndsWith(".part", StringComparison.Ordinal) || file.EndsWith(".ytdl", StringComparison.Ordinal))
                {
                    System.IO.File.Delete(file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "No se pudieron limpiar los ficheros parciales de {Id}", videoId);
        }
    }

    /// <summary>
    /// Toma la última línea de la salida de yt-dlp (<c>--print after_move:filepath</c>) y comprueba que es un fichero
    /// que existe dentro de la carpeta de caché y que pertenece a este vídeo.
    /// </summary>
    private string? FindDownloadedFile(string stdout, string videoId)
    {
        var line = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
        if (line is null)
        {
            return null;
        }

        var full = Path.GetFullPath(line, CacheFolder);
        return SafePath(Path.GetFileName(full)) == full
            && Path.GetFileNameWithoutExtension(full) == videoId
            && System.IO.File.Exists(full)
                ? full
                : null;
    }

    /// <summary>
    /// Une un nombre de fichero con la carpeta de caché, o devuelve null si no es un nombre simple.
    /// </summary>
    private string? SafePath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName)
        {
            return null;
        }

        return Path.Combine(Path.GetFullPath(CacheFolder), fileName);
    }

    private Dictionary<string, YoutubeVideo> Load()
    {
        if (_videos is not null)
        {
            return _videos;
        }

        _videos = new Dictionary<string, YoutubeVideo>(StringComparer.Ordinal);
        if (!System.IO.File.Exists(_stateFile))
        {
            return _videos;
        }

        try
        {
            using var stream = System.IO.File.OpenRead(_stateFile);
            var list = JsonSerializer.Deserialize<List<YoutubeVideo>>(stream, ChannelJson.Options) ?? [];
            foreach (var video in list.Where(v => YoutubeId.IsValid(v.Id)))
            {
                _videos[video.Id] = video;
            }
        }
        catch (JsonException ex)
        {
            var backup = _stateFile + ".corrupt";
            System.IO.File.Copy(_stateFile, backup, overwrite: true);
            _logger.LogError(ex, "No se pudo leer {Path}; copia en {Backup}", _stateFile, backup);
        }

        return _videos;
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(Load().Values.OrderBy(v => v.Id, StringComparer.Ordinal).ToList(), ChannelJson.Options);
        if (json == _lastSavedJson)
        {
            return;
        }

        Directory.CreateDirectory(DataFolder);
        var temp = _stateFile + ".tmp";
        System.IO.File.WriteAllText(temp, json);
        System.IO.File.Move(temp, _stateFile, overwrite: true);
        _lastSavedJson = json;
    }
}
