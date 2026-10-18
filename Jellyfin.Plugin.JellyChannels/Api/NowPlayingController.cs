using System;
using System.Globalization;
using System.Linq;
using System.Net.Mime;
using Jellyfin.Plugin.JellyChannels.Models;
using Jellyfin.Plugin.JellyChannels.Services;
using Microsoft.AspNetCore.Authorization;
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

    /// <summary>
    /// Initializes a new instance of the <see cref="NowPlayingController"/> class.
    /// </summary>
    /// <param name="store">Almacén de canales.</param>
    /// <param name="scheduleService">Servicio de parrilla.</param>
    public NowPlayingController(ChannelStore store, ScheduleService scheduleService)
    {
        _store = store;
        _scheduleService = scheduleService;
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
    /// Redirige al stream del item que se emite ahora (para el tuner M3U de Live TV).
    /// Se sirve en direct play (<c>static=true</c>), así que empieza desde el principio del item.
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

        return Redirect(string.Create(CultureInfo.InvariantCulture, $"{Request.BaseUrl()}/Videos/{current.Item.ItemId:N}/stream?static=true"));
    }
}
