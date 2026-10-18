using System.Net.Mime;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyChannels.Models;
using Jellyfin.Plugin.JellyChannels.Services;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyChannels.Api;

/// <summary>
/// CRUD de canales.
/// </summary>
[ApiController]
[Route("JellyChannels/channels")]
[Authorize]
public class ChannelsController : ControllerBase
{
    private readonly ChannelStore _store;
    private readonly ScheduleService _scheduleService;
    private readonly ILibraryResolver _resolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChannelsController"/> class.
    /// </summary>
    /// <param name="store">Almacén de canales.</param>
    /// <param name="scheduleService">Servicio de parrilla.</param>
    /// <param name="resolver">Resolver de biblioteca.</param>
    public ChannelsController(ChannelStore store, ScheduleService scheduleService, ILibraryResolver resolver)
    {
        _store = store;
        _scheduleService = scheduleService;
        _resolver = resolver;
    }

    /// <summary>
    /// Lista los canales.
    /// </summary>
    /// <returns>Canales ordenados por número.</returns>
    [HttpGet]
    [Produces(MediaTypeNames.Application.Json)]
    public ActionResult GetChannels() => RequestExtensions.PluginJson(_store.GetAll());

    /// <summary>
    /// Devuelve un canal.
    /// </summary>
    /// <param name="id">Id del canal.</param>
    /// <returns>El canal.</returns>
    [HttpGet("{id}")]
    [Produces(MediaTypeNames.Application.Json)]
    public ActionResult GetChannel([FromRoute] string id)
    {
        var channel = _store.Get(id);
        return channel is null ? NotFound() : RequestExtensions.PluginJson(channel);
    }

    /// <summary>
    /// Crea un canal.
    /// </summary>
    /// <returns>El canal creado.</returns>
    [HttpPost]
    [Authorize(Policy = Policies.RequiresElevation)]
    [Consumes(MediaTypeNames.Application.Json)]
    public async Task<ActionResult> CreateChannel()
    {
        var channel = await ReadChannelAsync().ConfigureAwait(false);
        if (channel is null)
        {
            return BadRequest("Cuerpo JSON de canal no válido.");
        }

        var errors = ChannelValidator.Validate(channel);
        if (errors.Count > 0)
        {
            return BadRequest(string.Join(' ', errors));
        }

        var created = _store.Create(channel);
        return RequestExtensions.PluginJson(created, StatusCodes.Status201Created);
    }

    /// <summary>
    /// Reemplaza un canal.
    /// </summary>
    /// <param name="id">Id del canal.</param>
    /// <returns>El canal guardado.</returns>
    [HttpPut("{id}")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [Consumes(MediaTypeNames.Application.Json)]
    public async Task<ActionResult> UpdateChannel([FromRoute] string id)
    {
        var channel = await ReadChannelAsync().ConfigureAwait(false);
        if (channel is null)
        {
            return BadRequest("Cuerpo JSON de canal no válido.");
        }

        var errors = ChannelValidator.Validate(channel);
        if (errors.Count > 0)
        {
            return BadRequest(string.Join(' ', errors));
        }

        var updated = _store.Update(id, channel);
        return updated is null ? NotFound() : RequestExtensions.PluginJson(updated);
    }

    /// <summary>
    /// Borra un canal.
    /// </summary>
    /// <param name="id">Id del canal.</param>
    /// <returns>204 si se borró.</returns>
    [HttpDelete("{id}")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public ActionResult DeleteChannel([FromRoute] string id) => _store.Delete(id) ? NoContent() : NotFound();

    /// <summary>
    /// Devuelve los items resueltos de un canal, en el orden de las fuentes.
    /// </summary>
    /// <param name="id">Id del canal.</param>
    /// <returns>Items con duración.</returns>
    [HttpGet("{id}/items")]
    [Produces(MediaTypeNames.Application.Json)]
    public ActionResult GetItems([FromRoute] string id)
    {
        var channel = _store.Get(id);
        return channel is null ? NotFound() : RequestExtensions.PluginJson(_resolver.Resolve(channel));
    }

    /// <summary>
    /// Vacía la caché de parrillas (por ejemplo tras añadir contenido a la biblioteca).
    /// </summary>
    /// <returns>204.</returns>
    [HttpPost("refresh")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public ActionResult Refresh()
    {
        _scheduleService.Invalidate();
        return NoContent();
    }

    private async Task<Channel?> ReadChannelAsync()
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<Channel>(Request.Body, ChannelJson.Options, HttpContext.RequestAborted).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
