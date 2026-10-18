using System.IO;
using System.Net.Mime;
using Jellyfin.Plugin.JellyChannels.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;

namespace Jellyfin.Plugin.JellyChannels.Api;

/// <summary>
/// EPG (XMLTV), lista M3U y ficheros de la carpeta assets.
/// </summary>
[ApiController]
[Route("JellyChannels")]
public class GuideController : ControllerBase
{
    private static readonly FileExtensionContentTypeProvider _contentTypes = new();

    private readonly GuideService _guideService;
    private readonly ChannelStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="GuideController"/> class.
    /// </summary>
    /// <param name="guideService">Servicio de guía.</param>
    /// <param name="store">Almacén de canales.</param>
    public GuideController(GuideService guideService, ChannelStore store)
    {
        _guideService = guideService;
        _store = store;
    }

    /// <summary>
    /// EPG en formato XMLTV.
    /// </summary>
    /// <returns>Documento XMLTV.</returns>
    [HttpGet("epg.xml")]
    [AllowAnonymous]
    [Produces(MediaTypeNames.Application.Xml)]
    public ActionResult GetEpg()
    {
        if (!this.CanReadGuide())
        {
            return Unauthorized();
        }

        return Content(_guideService.GetEpg(Request.GuideUrls()), "application/xml; charset=utf-8");
    }

    /// <summary>
    /// Lista de canales M3U.
    /// </summary>
    /// <returns>Contenido M3U.</returns>
    [HttpGet("channels.m3u")]
    [AllowAnonymous]
    [Produces("audio/x-mpegurl")]
    public ActionResult GetM3u()
    {
        if (!this.CanReadGuide())
        {
            return Unauthorized();
        }

        return Content(_guideService.GetM3u(Request.GuideUrls()), "audio/x-mpegurl; charset=utf-8");
    }

    /// <summary>
    /// Fichero de la carpeta assets (logos, marcas de agua).
    /// </summary>
    /// <param name="file">Nombre del fichero.</param>
    /// <returns>El fichero.</returns>
    [HttpGet("assets/{file}")]
    [AllowAnonymous]
    public ActionResult GetAsset([FromRoute] string file)
    {
        if (!RequestExtensions.IsPlainFileName(file))
        {
            return BadRequest();
        }

        var path = Path.Combine(_store.AssetsFolder, file);
        if (!System.IO.File.Exists(path))
        {
            return NotFound();
        }

        if (!_contentTypes.TryGetContentType(file, out var contentType))
        {
            contentType = MediaTypeNames.Application.Octet;
        }

        return PhysicalFile(path, contentType);
    }
}
