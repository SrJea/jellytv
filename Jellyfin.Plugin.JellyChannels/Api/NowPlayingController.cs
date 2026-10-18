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
    private readonly YoutubeCache _youtube;

    /// <summary>
    /// Initializes a new instance of the <see cref="NowPlayingController"/> class.
    /// </summary>
    /// <param name="store">Almacén de canales.</param>
    /// <param name="scheduleService">Servicio de parrilla.</param>
    /// <param name="remuxService">Servicio de remux.</param>
    /// <param name="youtube">Caché de vídeos de YouTube.</param>
    public NowPlayingController(ChannelStore store, ScheduleService scheduleService, RemuxService remuxService, YoutubeCache youtube)
    {
        _store = store;
        _scheduleService = scheduleService;
        _remuxService = remuxService;
        _youtube = youtube;
    }

    /// <summary>
    /// Item actual, offset dentro de él y siguiente item. Si el item que toca es un vídeo de YouTube que aún no
    /// está descargado, se salta: se devuelve el siguiente item reproducible, desde el principio, con
    /// <c>skipped</c> a true.
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
        var scheduled = schedule.GetEntryAt(now);
        if (scheduled is null)
        {
            return NoContent();
        }

        var urls = Request.GuideUrls();
        var (current, skipped) = FirstPlayable(schedule, scheduled);
        if (current is null)
        {
            // Nada reproducible ahora mismo: se informa del hueco que toca, marcado como no disponible.
            return RequestExtensions.PluginJson(new NowPlayingDto(
                channel.Id,
                now,
                Describe(scheduled, urls),
                scheduled.OffsetTicksAt(now),
                Describe(schedule.GetNext(scheduled), urls),
                schedule.ItemCount,
                schedule.CycleTicks));
        }

        return RequestExtensions.PluginJson(new NowPlayingDto(
            channel.Id,
            now,
            Describe(current, urls),
            skipped ? 0 : current.OffsetTicksAt(now),
            Describe(schedule.GetNext(current), urls),
            schedule.ItemCount,
            schedule.CycleTicks,
            skipped));
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
        var urls = Request.GuideUrls();
        var entries = _scheduleService.GetSchedule(channel)
            .GetEntries(now, now.AddHours(Math.Clamp(hours, 1, 336)))
            .Take(5000)
            .Select(entry => Describe(entry, urls))
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

        var schedule = _scheduleService.GetSchedule(channel);
        var scheduled = schedule.GetEntryAt(_scheduleService.Now);
        if (scheduled is null)
        {
            return NotFound();
        }

        if (_remuxService.IsEnabled)
        {
            return Redirect(Request.GuideUrls().Live(channel));
        }

        var current = FirstPlayable(schedule, scheduled).Entry;
        if (current is null)
        {
            return NotFound();
        }

        if (current.Item.YoutubeId is { } videoId)
        {
            return Redirect(Request.GuideUrls().YoutubeFile(videoId));
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

    private bool IsAvailable(ScheduleItem item) => item.YoutubeId is null || _youtube.GetFilePath(item.YoutubeId) is not null;

    /// <summary>
    /// Devuelve el primer hueco reproducible a partir de <paramref name="entry"/>, saltando los vídeos de
    /// YouTube sin descargar.
    /// </summary>
    private (ScheduleEntry? Entry, bool Skipped) FirstPlayable(ScheduleEngine schedule, ScheduleEntry entry)
    {
        var candidate = entry;
        for (int i = 0; i < schedule.ItemCount; i++)
        {
            if (IsAvailable(candidate.Item))
            {
                return (candidate, i > 0);
            }

            candidate = schedule.GetNext(candidate);
        }

        return (null, false);
    }

    private ScheduleEntryDto Describe(ScheduleEntry entry, GuideUrls urls)
    {
        bool available = IsAvailable(entry.Item);
        return ScheduleEntryDto.From(entry, available, available && entry.Item.YoutubeId is { } videoId ? urls.YoutubeFile(videoId) : null);
    }
}
