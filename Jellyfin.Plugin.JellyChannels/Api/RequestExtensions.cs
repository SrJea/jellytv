using System;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.JellyChannels.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyChannels.Api;

/// <summary>
/// Utilidades compartidas por los controladores.
/// </summary>
internal static class RequestExtensions
{
    /// <summary>
    /// URL base pública del servidor (incluye la BaseUrl de Jellyfin).
    /// </summary>
    /// <param name="request">Petición.</param>
    /// <returns>URL sin barra final.</returns>
    public static string BaseUrl(this HttpRequest request)
        => $"{request.Scheme}://{request.Host}{request.PathBase}".TrimEnd('/');

    /// <summary>
    /// Constructor de URLs que propaga la clave de API de la petición, si la trae.
    /// </summary>
    /// <param name="request">Petición.</param>
    /// <returns>Constructor de URLs.</returns>
    public static GuideUrls GuideUrls(this HttpRequest request)
    {
        string? apiKey = request.Query["ApiKey"].FirstOrDefault() ?? request.Query["api_key"].FirstOrDefault();
        return new GuideUrls(request.BaseUrl(), string.IsNullOrEmpty(apiKey) ? null : apiKey);
    }

    /// <summary>
    /// Indica si la petición puede acceder a los endpoints de guía.
    /// </summary>
    /// <param name="controller">Controlador.</param>
    /// <returns>True si está permitido.</returns>
    public static bool CanReadGuide(this ControllerBase controller)
        => (Plugin.Instance?.Configuration.AllowAnonymousGuide ?? false) || controller.User.Identity?.IsAuthenticated == true;

    /// <summary>
    /// Serializa con las opciones JSON del plugin.
    /// </summary>
    /// <param name="value">Valor.</param>
    /// <returns>Resultado JSON.</returns>
    public static ContentResult PluginJson(object value)
        => new() { Content = JsonSerializer.Serialize(value, ChannelJson.Options), ContentType = "application/json; charset=utf-8", StatusCode = StatusCodes.Status200OK };

    /// <summary>
    /// Igual que <see cref="PluginJson(object)"/> con otro código de estado.
    /// </summary>
    /// <param name="value">Valor.</param>
    /// <param name="statusCode">Código HTTP.</param>
    /// <returns>Resultado JSON.</returns>
    public static ContentResult PluginJson(object value, int statusCode)
    {
        var result = PluginJson(value);
        result.StatusCode = statusCode;
        return result;
    }

    /// <summary>
    /// Comprueba que una cadena sea un nombre de fichero simple, sin rutas.
    /// </summary>
    /// <param name="file">Nombre.</param>
    /// <returns>True si es seguro.</returns>
    public static bool IsPlainFileName(string? file)
        => !string.IsNullOrWhiteSpace(file)
            && string.Equals(System.IO.Path.GetFileName(file), file, StringComparison.Ordinal)
            && file.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) < 0
            && file != "." && file != "..";
}
