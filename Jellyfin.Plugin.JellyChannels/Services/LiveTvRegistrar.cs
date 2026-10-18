using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Security;
using MediaBrowser.Model.LiveTv;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyChannels.Services;

/// <summary>
/// Registra el tuner M3U y la guía XMLTV de JellyChannels en Live TV de Jellyfin y los mantiene al día, para
/// que los canales aparezcan solos en las listas de Live TV sin tener que añadirlos a mano. Usa los gestores de
/// Live TV de Jellyfin y una clave de API propia ("JellyChannels (Live TV)") que crea y revoca él mismo.
/// </summary>
public sealed class LiveTvRegistrar : BackgroundService
{
    private const string ApiKeyName = "JellyChannels (Live TV)";
    private static readonly TimeSpan _startupDelay = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan _debounce = TimeSpan.FromSeconds(5);

    private readonly ITunerHostManager _tuners;
    private readonly IListingsManager _listings;
    private readonly IServerConfigurationManager _config;
    private readonly IServerApplicationHost _host;
    private readonly IAuthenticationManager _auth;
    private readonly ITaskManager _tasks;
    private readonly ChannelStore _store;
    private readonly ILogger<LiveTvRegistrar> _logger;
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly Lock _flagsLock = new();
    private bool _syncRequested;
    private bool _guideRefreshRequested;

    /// <summary>
    /// Initializes a new instance of the <see cref="LiveTvRegistrar"/> class.
    /// </summary>
    /// <param name="tuners">Gestor de tuners de Live TV.</param>
    /// <param name="listings">Gestor de guías de Live TV.</param>
    /// <param name="config">Configuración del servidor.</param>
    /// <param name="host">Servidor, que sabe su dirección local.</param>
    /// <param name="auth">Gestor de claves de API.</param>
    /// <param name="tasks">Gestor de tareas programadas.</param>
    /// <param name="store">Almacén de canales.</param>
    /// <param name="logger">Logger.</param>
    public LiveTvRegistrar(
        ITunerHostManager tuners,
        IListingsManager listings,
        IServerConfigurationManager config,
        IServerApplicationHost host,
        IAuthenticationManager auth,
        ITaskManager tasks,
        ChannelStore store,
        ILogger<LiveTvRegistrar> logger)
    {
        _tuners = tuners;
        _listings = listings;
        _config = config;
        _host = host;
        _auth = auth;
        _tasks = tasks;
        _store = store;
        _logger = logger;
        _store.Changed += (_, _) => Request(sync: false, refreshGuide: true);
    }

    /// <summary>
    /// Pide un repaso del registro y, opcionalmente, un refresco de la guía.
    /// </summary>
    /// <param name="sync">Comprobar que el tuner y la guía están registrados con la URL correcta.</param>
    /// <param name="refreshGuide">Refrescar la guía de Live TV aunque no cambie el registro.</param>
    public void Request(bool sync, bool refreshGuide)
    {
        lock (_flagsLock)
        {
            _syncRequested |= sync;
            _guideRefreshRequested |= refreshGuide;
        }

        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // Ya había un aviso pendiente; los indicadores de arriba lo recogerán todo.
        }
    }

    /// <summary>
    /// Lee en qué estado está el registro.
    /// </summary>
    /// <returns>El estado.</returns>
    public async Task<LiveTvStatus> GetStatusAsync()
    {
        var live = ReadLiveTvOptions();
        var keys = await _auth.GetApiKeys().ConfigureAwait(false);
        return new LiveTvStatus(
            Plugin.Instance?.Configuration.AutoRegisterLiveTv ?? false,
            live.TunerHosts.Any(LiveTvPlan.IsOurTuner),
            live.ListingProviders.Any(LiveTvPlan.IsOurListings),
            keys.Any(k => k.AppName == ApiKeyName && k.DateRevoked is null));
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
        if (Plugin.Instance is { } plugin)
        {
            plugin.ConfigurationChanged += (_, _) => Request(sync: true, refreshGuide: false);
        }

        // Al arrancar se repasa una vez; la espera deja que Live TV y la base de datos estén listos.
        Request(sync: true, refreshGuide: false);
        try
        {
            await Task.Delay(_startupDelay, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _wake.WaitAsync(stoppingToken).ConfigureAwait(false);

                // Agrupa ráfagas de cambios (por ejemplo, varios canales guardados seguidos).
                await Task.Delay(_debounce, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            bool sync;
            bool refreshGuide;
            lock (_flagsLock)
            {
                sync = _syncRequested;
                refreshGuide = _guideRefreshRequested;
                _syncRequested = false;
                _guideRefreshRequested = false;
            }

            try
            {
                bool changed = sync && await SyncAsync().ConfigureAwait(false);

                // Si el registro cambió, Jellyfin ya borra la copia de la guía y encola el refresco.
                if (refreshGuide && !changed && (Plugin.Instance?.Configuration.AutoRegisterLiveTv ?? false))
                {
                    await RefreshGuideAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _logger.LogError(ex, "No se pudo sincronizar JellyChannels con Live TV");
            }
        }
    }

    /// <summary>
    /// Deja Live TV como corresponde según los ajustes: registrado si el registro automático está activado
    /// y sin rastro si no lo está.
    /// </summary>
    /// <returns>True si cambió algo en Live TV.</returns>
    private async Task<bool> SyncAsync()
    {
        var live = ReadLiveTvOptions();
        if (!(Plugin.Instance?.Configuration.AutoRegisterLiveTv ?? false))
        {
            return await RemoveAsync(live).ConfigureAwait(false);
        }

        var key = await EnsureApiKeyAsync().ConfigureAwait(false);
        var baseUrl = _host.GetLocalApiUrl("127.0.0.1", Uri.UriSchemeHttp, _host.HttpPort);
        var m3u = $"{baseUrl}{LiveTvPlan.M3uPath}?ApiKey={Uri.EscapeDataString(key)}";
        var epg = $"{baseUrl}{LiveTvPlan.EpgPath}?ApiKey={Uri.EscapeDataString(key)}";

        var plan = LiveTvPlan.Decide(live.TunerHosts, live.ListingProviders, m3u, epg);
        if (!plan.HasChanges)
        {
            return false;
        }

        foreach (var id in plan.DuplicateTunerIds)
        {
            _logger.LogInformation("Se quita un tuner de JellyChannels repetido de Live TV");
            _tuners.DeleteTunerHost(id);
        }

        foreach (var id in plan.DuplicateListingIds)
        {
            _logger.LogInformation("Se quita una guía de JellyChannels repetida de Live TV");
            _listings.DeleteListingsProvider(id);
        }

        if (plan.Tuner != LiveTvAction.None)
        {
            var tuner = plan.ExistingTuner is null ? new TunerHostInfo { FriendlyName = LiveTvPlan.TunerName, TunerCount = 0 } : Copy(plan.ExistingTuner);
            tuner.Type = "m3u";
            tuner.Url = m3u;
            _logger.LogInformation("{Action} el tuner de JellyChannels en Live TV", plan.Tuner == LiveTvAction.Create ? "Se añade" : "Se actualiza");
            await _tuners.SaveTunerHost(tuner).ConfigureAwait(false);
        }

        if (plan.Listings != LiveTvAction.None)
        {
            var listings = plan.ExistingListings is null ? new ListingsProviderInfo { EnableAllTuners = true } : Copy(plan.ExistingListings);
            listings.Type = "xmltv";
            listings.Path = epg;
            _logger.LogInformation("{Action} la guía de JellyChannels en Live TV", plan.Listings == LiveTvAction.Create ? "Se añade" : "Se actualiza");
            await _listings.SaveListingProvider(listings, validateLogin: false, validateListings: false).ConfigureAwait(false);
        }

        return true;
    }

    private async Task<bool> RemoveAsync(LiveTvOptions live)
    {
        bool changed = false;
        foreach (var tuner in live.TunerHosts.Where(LiveTvPlan.IsOurTuner))
        {
            _logger.LogInformation("Se quita el tuner de JellyChannels de Live TV (registro automático desactivado)");
            _tuners.DeleteTunerHost(tuner.Id);
            changed = true;
        }

        foreach (var listings in live.ListingProviders.Where(LiveTvPlan.IsOurListings))
        {
            _logger.LogInformation("Se quita la guía de JellyChannels de Live TV (registro automático desactivado)");
            _listings.DeleteListingsProvider(listings.Id);
            changed = true;
        }

        foreach (var key in (await _auth.GetApiKeys().ConfigureAwait(false)).Where(k => k.AppName == ApiKeyName))
        {
            await _auth.DeleteApiKey(key.AccessToken).ConfigureAwait(false);
        }

        return changed;
    }

    private async Task<string> EnsureApiKeyAsync()
    {
        var existing = (await _auth.GetApiKeys().ConfigureAwait(false)).FirstOrDefault(k => k.AppName == ApiKeyName && k.DateRevoked is null);
        if (existing is null)
        {
            _logger.LogInformation("Se crea la clave de API «{Name}»", ApiKeyName);
            await _auth.CreateApiKey(ApiKeyName).ConfigureAwait(false);
            existing = (await _auth.GetApiKeys().ConfigureAwait(false)).First(k => k.AppName == ApiKeyName && k.DateRevoked is null);
        }

        return existing.AccessToken;
    }

    /// <summary>
    /// Hace que Live TV vuelva a leer el EPG. Jellyfin guarda el XMLTV descargado una hora, así que solo encolar
    /// la tarea de refresco dejaría los programas de un canal editado sin actualizar hasta una hora después.
    /// Guardar de nuevo la guía, sin cambiarla, borra esa copia y encola el refresco.
    /// </summary>
    private async Task RefreshGuideAsync()
    {
        var existing = ReadLiveTvOptions().ListingProviders.FirstOrDefault(LiveTvPlan.IsOurListings);
        if (existing is not null)
        {
            _logger.LogInformation("Se refresca la guía de Live TV para recoger los cambios de canales");
            await _listings.SaveListingProvider(Copy(existing), validateLogin: false, validateListings: false).ConfigureAwait(false);
            return;
        }

        var worker = _tasks.ScheduledTasks.FirstOrDefault(t => t.ScheduledTask.Key == "RefreshGuide");
        if (worker is not null)
        {
            _tasks.QueueScheduledTask(worker.ScheduledTask, new TaskOptions());
        }
    }

    private LiveTvOptions ReadLiveTvOptions() => (LiveTvOptions)_config.GetConfiguration("livetv");

    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value))!;
}
