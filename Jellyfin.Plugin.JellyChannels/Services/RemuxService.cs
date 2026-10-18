using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyChannels.Services;

/// <summary>
/// Stream continuo de un canal: encadena tramos <c>ffmpeg -c copy</c> siguiendo la parrilla.
/// Solo hay procesos mientras alguien está viendo, y es opcional.
/// </summary>
public class RemuxService
{
    private const int DefaultMaxStreams = 2;
    private const int MaxFailuresPerSlot = 3;
    private const int MaxBadSlotsInARow = 3;
    private static readonly TimeSpan _minSlot = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan _retryDelay = TimeSpan.FromSeconds(1);

    // Hueco entre el último PTS de un tramo y el primero del siguiente: más o menos un fotograma.
    private static readonly TimeSpan _frameGap = TimeSpan.FromMilliseconds(40);

    private readonly IFfmpegRunner _runner;
    private readonly ILogger<RemuxService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly Func<int> _maxStreams;
    private readonly Lock _lock = new();
    private int _active;

    /// <summary>
    /// Initializes a new instance of the <see cref="RemuxService"/> class.
    /// </summary>
    /// <param name="runner">Ejecutor de ffmpeg.</param>
    /// <param name="logger">Logger.</param>
    public RemuxService(IFfmpegRunner runner, ILogger<RemuxService> logger)
        : this(runner, logger, TimeProvider.System, () => Plugin.Instance?.Configuration.MaxRemuxStreams ?? DefaultMaxStreams)
    {
    }

    internal RemuxService(IFfmpegRunner runner, ILogger<RemuxService> logger, TimeProvider timeProvider, Func<int> maxStreams)
    {
        _runner = runner;
        _logger = logger;
        _timeProvider = timeProvider;
        _maxStreams = maxStreams;
    }

    /// <summary>
    /// Gets a value indicating whether el remux está activado en los ajustes y hay ffmpeg.
    /// </summary>
    public bool IsEnabled => (Plugin.Instance?.Configuration.EnableRemux ?? false) && _runner.IsAvailable;

    /// <summary>
    /// Gets el número de streams abiertos ahora mismo.
    /// </summary>
    public int ActiveStreams
    {
        get
        {
            lock (_lock)
            {
                return _active;
            }
        }
    }

    /// <summary>
    /// Reserva un hueco para un stream nuevo.
    /// </summary>
    /// <returns>Un objeto que libera el hueco al desecharse, o null si ya están todos ocupados.</returns>
    public IDisposable? TryEnter()
    {
        lock (_lock)
        {
            if (_active >= Math.Max(1, _maxStreams()))
            {
                return null;
            }

            _active++;
            return new Lease(this);
        }
    }

    /// <summary>
    /// Emite el canal en <paramref name="output"/> hasta que se cancele o no quede nada que emitir.
    /// </summary>
    /// <param name="getSchedule">Parrilla actual; se consulta en cada tramo para recoger cambios de la biblioteca.</param>
    /// <param name="output">Destino del MPEG-TS.</param>
    /// <param name="cancellationToken">Cancelación (el cliente se desconectó).</param>
    /// <returns>Una tarea que acaba al terminar el stream.</returns>
    public async Task StreamAsync(Func<ScheduleEngine> getSchedule, Stream output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(getSchedule);
        ArgumentNullException.ThrowIfNull(output);

        var streamStart = _timeProvider.GetUtcNow();
        var timelineEnd = TimeSpan.Zero;
        int failuresInSlot = 0;
        int badSlots = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var now = _timeProvider.GetUtcNow();
            var entry = getSchedule().GetEntryAt(now);
            if (entry is null)
            {
                return;
            }

            var remaining = entry.End - now;
            if (remaining < _minSlot)
            {
                await DelayAsync(remaining, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (string.IsNullOrEmpty(entry.Item.Path))
            {
                _logger.LogWarning("«{Title}» no es un fichero local y no se puede remuxar; se salta su hueco", entry.Item.Title);
                if (++badSlots >= MaxBadSlotsInARow)
                {
                    return;
                }

                await DelayAsync(entry.End - _timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
                continue;
            }

            // La línea de tiempo del stream nunca retrocede: con -c copy un tramo puede empezar hasta
            // un GOP antes del offset pedido y acabar después de su hueco.
            var timestamp = TimeSpan.FromTicks(Math.Max((now - streamStart).Ticks, timelineEnd.Ticks));
            var request = new RemuxRequest(entry.Item.Path, TimeSpan.FromTicks(entry.OffsetTicksAt(now)), remaining, timestamp);

            var runStart = _timeProvider.GetTimestamp();
            var result = await _runner.RunAsync(request, output, cancellationToken).ConfigureAwait(false);
            var elapsed = _timeProvider.GetElapsedTime(runStart);

            // Un tramo puede contener más tiempo del que tarda en emitirse (el contenido anterior al
            // salto de -ss sale de golpe), así que si el ejecutor sabe el último PTS se usa ese.
            timelineEnd = TimeSpan.FromTicks(Math.Max(timelineEnd.Ticks, (result.LastTimestamp is TimeSpan last ? last + _frameGap : timestamp + elapsed).Ticks));

            if (result.ExitCode == 0 && result.BytesWritten > 0)
            {
                failuresInSlot = 0;
                badSlots = 0;
                continue;
            }

            if (++failuresInSlot < MaxFailuresPerSlot)
            {
                await DelayAsync(_retryDelay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            failuresInSlot = 0;
            if (++badSlots >= MaxBadSlotsInARow)
            {
                _logger.LogError("Se cierra el stream: {Count} huecos seguidos sin poder remuxarse", badSlots);
                return;
            }

            _logger.LogWarning("«{Title}» no se pudo remuxar; se espera al siguiente item", entry.Item.Title);
            await DelayAsync(entry.End - _timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    private void Release()
    {
        lock (_lock)
        {
            _active--;
        }
    }

    private sealed class Lease : IDisposable
    {
        private RemuxService? _owner;

        public Lease(RemuxService owner) => _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release();
    }
}
