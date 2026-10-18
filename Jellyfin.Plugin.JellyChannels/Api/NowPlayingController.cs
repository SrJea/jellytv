using System;
using System.Globalization;
using System.Linq;
using System.Net.Mime;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyChannels.Models;
using Jellyfin.Plugin.JellyChannels.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyChannels.Api;

/// <summary>
/// Qué se emite en un canal y cuándo.
/// </summary>
[ApiController]
[Route("JellyChannels/channels/{id}")]
[Authorize]
public class NowPlayingController : ControllerBase
{
    private readonly ChannelStore _store;
    private readonly ScheduleService _scheduleService;
    private readonly RemuxService _remuxService;

    /// <summary>
    /// Initializes a new instance of the <see cref="NowPlayingController"/> class.
    /// </summary>
    /// <param name="store">Almacén de canales.</param>
    /// <param name="scheduleService">Servicio de parrilla.</param>
    /// <param name="remuxService">Servicio de remux.</param>
    public NowPlayingController(ChannelStore store, ScheduleService scheduleService, RemuxService remuxService)
    {
        _store = store;
        _scheduleService = scheduleService;
        _remuxService = remuxService;
    }

    /// <summary>
    /// Item actual, offset dentro de él y siguiente item.
    /// </summary>
    /// <param name="id">Id del canal.</param>
    /// <returns>El estado del canal.</returns>
    [HttpGet("now")]
    [Produces(MediaTypeNames.Application.Json)]
    public ActionResult GetNowPlaying([FromRoute] string id)
    {
        var channel = _store.Get(id);
        if (channel is null)
        {
            return NotFound();
        }

        var schedule = _scheduleService.GetSchedule(channel);
        var now = _scheduleService.Now;
        var current = schedule.GetEntryAt(now);
        if (current is null)
        {
            return NoContent();
        }

        var next = schedule.GetNext(current);
        return RequestExtensions.PluginJson(new NowPlayingDto(
            channel.Id,
            now,
            ScheduleEntryDto.From(current),
            current.OffsetTicksAt(now),
            ScheduleEntryDto.From(next)));
    }

    /// <summary>
    /// Parrilla del canal desde ahora.
    /// </summary>
    /// <param name="id">Id del canal.</param>
    /// <param name="hours">Horas hacia delante (1-336).</param>
    /// <returns>Huecos de la parrilla.</returns>
    [HttpGet("schedule")]
    [Produces(MediaTypeNames.Application.Json)]
    public ActionResult GetSchedule([FromRoute] string id, [FromQuery] int hours = 24)
    {
        var channel = _store.Get(id);
        if (channel is null)
        {
            return NotFound();
        }

        var now = _scheduleService.Now;
        var entries = _scheduleService.GetSchedule(channel)
            .GetEntries(now, now.AddHours(Math.Clamp(hours, 1, 336)))
            .Take(5000)
            .Select(ScheduleEntryDto.From)
            .ToList();
        return RequestExtensions.PluginJson(entries);
    }

    /// <summary>
    /// Redirige al stream del canal (para el tuner M3U de Live TV): al stream continuo <c>live.ts</c> si el
    /// remux está activado y, si no, al item que se emite ahora en direct play (<c>static=true</c>), que
    /// empieza siempre desde el principio del item.
    /// </summary>
    /// <param name="id">Id del canal.</param>
    /// <returns>Redirección 302.</returns>
    [HttpGet("stream")]
    [HttpHead("stream")]
    [AllowAnonymous]
    public ActionResult GetStream([FromRoute] string id)
    {
        if (!this.CanReadGuide())
        {
            return Unauthorized();
        }

        var channel = _store.Get(id);
        if (channel is null)
        {
            return NotFound();
        }

        var current = _scheduleService.GetSchedule(channel).GetEntryAt(_scheduleService.Now);
        if (current is null)
        {
            return NotFound();
        }

        if (_remuxService.IsEnabled)
        {
            return Redirect(Request.GuideUrls().Live(channel));
        }

        return Redirect(string.Create(CultureInfo.InvariantCulture, $"{Request.BaseUrl()}/Videos/{current.Item.ItemId:N}/stream?static=true"));
    }

    /// <summary>
    /// Stream MPEG-TS continuo del canal: remuxa los ficheros con <c>ffmpeg -c copy</c>, sin transcodificar,
    /// y empieza donde toca según la parrilla. Requiere activar el remux en los ajustes.
    /// </summary>
    /// <param name="id">Id del canal.</param>
    /// <returns>El stream, hasta que el cliente se desconecta.</returns>
    [HttpGet("live.ts")]
    [HttpHead("live.ts")]
    [AllowAnonymous]
    public async Task<ActionResult> GetLive([FromRoute] string id)
    {
        if (!this.CanReadGuide())
        {
            return Unauthorized();
        }

        if (!_remuxService.IsEnabled)
        {
            return NotFound("El remux no está activado en los ajustes del plugin o no hay ffmpeg.");
        }

        var channel = _store.Get(id);
        if (channel is null)
        {
            return NotFound();
        }

        if (_scheduleService.GetSchedule(channel).IsEmpty)
        {
            return NotFound("El canal no tiene items.");
        }

        if (HttpMethods.IsHead(Request.Method))
        {
            return new FileContentResult([], "video/mp2t");
        }

        using var lease = _remuxService.TryEnter();
        if (lease is null)
        {
            Response.Headers.RetryAfter = "30";
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "Se alcanzó el máximo de streams remuxados simultáneos.");
        }

        Response.ContentType = "video/mp2t";
        Response.Headers.CacheControl = "no-store";
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        try
        {
            await _remuxService.StreamAsync(() => _scheduleService.GetSchedule(channel), Response.Body, HttpContext.RequestAborted).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // El cliente se desconectó: es la forma normal de acabar.
        }

        return new EmptyResult();
    }
}
