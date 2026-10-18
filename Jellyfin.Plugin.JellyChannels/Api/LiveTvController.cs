using System.Net.Mime;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyChannels.Services;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyChannels.Api;

/// <summary>
/// Estado del registro automático de JellyChannels en Live TV.
/// </summary>
[ApiController]
[Route("JellyChannels/livetv")]
[Authorize(Policy = Policies.RequiresElevation)]
public class LiveTvController : ControllerBase
{
    private readonly LiveTvRegistrar _registrar;

    /// <summary>
    /// Initializes a new instance of the <see cref="LiveTvController"/> class.
    /// </summary>
    /// <param name="registrar">Registrador de Live TV.</param>
    public LiveTvController(LiveTvRegistrar registrar)
    {
        _registrar = registrar;
    }

    /// <summary>
    /// Dice si el tuner y la guía de JellyChannels están registrados en Live TV.
    /// </summary>
    /// <returns>El estado.</returns>
    [HttpGet]
    [Produces(MediaTypeNames.Application.Json)]
    public async Task<ActionResult> GetStatus() => RequestExtensions.PluginJson(await _registrar.GetStatusAsync().ConfigureAwait(false));

    /// <summary>
    /// Repasa el registro y refresca la guía de Live TV en unos segundos.
    /// </summary>
    /// <returns>204.</returns>
    [HttpPost("sync")]
    public ActionResult Sync()
    {
        _registrar.Request(sync: true, refreshGuide: true);
        return NoContent();
    }
}
