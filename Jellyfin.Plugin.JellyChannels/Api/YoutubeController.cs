using System;
using System.Linq;
using System.Net.Mime;
using Jellyfin.Plugin.JellyChannels.Models;
using Jellyfin.Plugin.JellyChannels.Services;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;

namespace Jellyfin.Plugin.JellyChannels.Api;

/// <summary>
/// Vídeos de YouTube: listado, servicio del fichero descargado y gestión manual de la caché.
/// </summary>
[ApiController]
[Route("JellyChannels/youtube")]
public class YoutubeController : ControllerBase
{
    private static readonly FileExtensionContentTypeProvider _contentTypes = new();

    private readonly YoutubeCache _cache;
    private readonly YoutubeWorker _worker;

    /// <summary>
    /// Initializes a new instance of the <see cref="YoutubeController"/> class.
    /// </summary>
    /// <param name="cache">Caché de YouTube.</param>
    /// <param name="worker">Tarea de fondo, a la que se avisa tras un reintento.</param>
    public YoutubeController(YoutubeCache cache, YoutubeWorker worker)
    {
        _cache = cache;
        _worker = worker;
    }

    /// <summary>
    /// Lista los vídeos de YouTube conocidos y el estado de su copia en la caché.
    /// </summary>
    /// <returns>Los vídeos.</returns>
    [HttpGet]
    [Authorize(Policy = Policies.RequiresElevation)]
    [Produces(MediaTypeNames.Application.Json)]
    public ActionResult GetVideos()
    {
        var videos = _cache.GetAll().Select(v => new
        {
            v.Id,
            v.Title,
            v.Uploader,
            v.DurationSeconds,
            v.Thumbnail,
            State = StateOf(v),
            v.SizeBytes,
            v.DownloadedAt,
            v.Retention,
            v.Failures,
            v.NextAttemptAt,
            v.LastError
        });
        return RequestExtensions.PluginJson(videos);
    }

    /// <summary>
    /// Sirve el vídeo descargado, con soporte de <c>Range</c> para poder empezar en un offset.
    /// </summary>
    /// <param name="id">Id de YouTube.</param>
    /// <returns>El fichero.</returns>
    [HttpGet("{id}/file")]
    [HttpHead("{id}/file")]
    [AllowAnonymous]
    public ActionResult GetFile([FromRoute] string id)
    {
        if (!this.CanReadGuide())
        {
            return Unauthorized();
        }

        if (!YoutubeId.IsValid(id))
        {
            return BadRequest();
        }

        var path = _cache.GetFilePath(id);
        if (path is null)
        {
            return NotFound("El vídeo aún no está descargado.");
        }

        if (!_contentTypes.TryGetContentType(path, out var contentType))
        {
            contentType = MediaTypeNames.Application.Octet;
        }

        return PhysicalFile(path, contentType, enableRangeProcessing: true);
    }

    /// <summary>
    /// Borra el fichero descargado (los metadatos se conservan y volverá a descargarse cuando toque).
    /// </summary>
    /// <param name="id">Id de YouTube.</param>
    /// <returns>204 si había un fichero.</returns>
    [HttpDelete("{id}/cache")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public ActionResult DeleteCache([FromRoute] string id)
        => YoutubeId.IsValid(id) && _cache.DeleteFile(id) ? NoContent() : NotFound();

    /// <summary>
    /// Olvida los fallos de un vídeo para que se reintente ya.
    /// </summary>
    /// <param name="id">Id de YouTube.</param>
    /// <returns>204 si el vídeo existe.</returns>
    [HttpPost("{id}/retry")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public ActionResult Retry([FromRoute] string id)
    {
        if (!YoutubeId.IsValid(id) || !_cache.ResetFailures(id))
        {
            return NotFound();
        }

        _worker.Wake();
        return NoContent();
    }

    private static string StateOf(YoutubeVideo video)
    {
        if (video.File is not null)
        {
            return "cached";
        }

        if (video.Failures > 0)
        {
            return "error";
        }

        return video.Title is null ? "pending" : "ready";
    }
}
