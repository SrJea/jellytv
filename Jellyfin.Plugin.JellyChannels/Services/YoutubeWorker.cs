using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyChannels.Services;

/// <summary>
/// Tarea ligera de fondo para YouTube: lee metadatos de los vídeos nuevos, descarga poco antes de que
/// toque emitir cada uno (una descarga a la vez) y borra lo que ya no hace falta. No hace nada mientras
/// las fuentes de YouTube estén desactivadas.
/// </summary>
public sealed class YoutubeWorker : BackgroundService
{
    private static readonly TimeSpan _tick = TimeSpan.FromSeconds(60);

    private readonly YoutubeCache _cache;
    private readonly YoutubeChannelUsage _usage;
    private readonly ScheduleService _scheduleService;
    private readonly ChannelStore _store;
    private readonly ILogger<YoutubeWorker> _logger;
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly Channel<string> _downloads = Channel.CreateUnbounded<string>();
    private readonly HashSet<string> _queued = [];
    private readonly Lock _queueLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="YoutubeWorker"/> class.
    /// </summary>
    /// <param name="cache">Caché de YouTube.</param>
    /// <param name="usage">Uso de YouTube por los canales.</param>
    /// <param name="scheduleService">Servicio de parrilla.</param>
    /// <param name="store">Almacén de canales.</param>
    /// <param name="logger">Logger.</param>
    public YoutubeWorker(YoutubeCache cache, YoutubeChannelUsage usage, ScheduleService scheduleService, ChannelStore store, ILogger<YoutubeWorker> logger)
    {
        _cache = cache;
        _usage = usage;
        _scheduleService = scheduleService;
        _store = store;
        _logger = logger;
        _store.Changed += (_, _) => Wake();
    }

    /// <summary>
    /// Despierta a la tarea para que repase ya, por ejemplo tras guardar un canal.
    /// </summary>
    public void Wake()
    {
        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // Ya había un aviso pendiente.
        }
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _wake.Dispose();
        base.Dispose();
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var downloader = DownloadLoopAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await RunOnceAsync(stoppingToken).ConfigureAwait(false))
                {
                    continue;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _logger.LogError(ex, "Error en el repaso de la caché de YouTube");
            }

            try
            {
                await _wake.WaitAsync(_tick, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _downloads.Writer.TryComplete();
        await downloader.ConfigureAwait(false);
    }

    /// <summary>
    /// Un repaso: metadatos, descargas y borrados.
    /// </summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>True si entraron vídeos nuevos en la parrilla y conviene repasar de nuevo enseguida.</returns>
    internal async Task<bool> RunOnceAsync(CancellationToken cancellationToken)
    {
        var settings = YoutubeSettings.FromConfiguration();
        var now = DateTimeOffset.UtcNow;
        var references = _usage.GetReferences();
        if (!settings.Enabled && references.Count == 0)
        {
            return false;
        }

        var plan = _cache.Maintain(references, _usage.GetNeeds(now, settings.PrefetchMargin), now);
        bool newVideos = false;
        foreach (var id in plan.MetadataToFetch)
        {
            newVideos |= await _cache.FetchMetadataAsync(id, cancellationToken).ConfigureAwait(false);
        }

        if (newVideos || plan.Deleted.Count > 0)
        {
            // Con metadatos nuevos el vídeo entra en la parrilla y puede que ya toque descargarlo.
            _scheduleService.Invalidate();
        }

        foreach (var id in plan.ToDownload)
        {
            lock (_queueLock)
            {
                if (!_queued.Add(id))
                {
                    continue;
                }
            }

            _downloads.Writer.TryWrite(id);
        }

        return newVideos;
    }

    private async Task DownloadLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var id in _downloads.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    if (await _cache.DownloadAsync(id, cancellationToken).ConfigureAwait(false))
                    {
                        _scheduleService.Invalidate();
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    _logger.LogError(ex, "Error al descargar el vídeo de YouTube {Id}", id);
                }
                finally
                {
                    lock (_queueLock)
                    {
                        _queued.Remove(id);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // La tarea se está parando.
        }
    }
}
