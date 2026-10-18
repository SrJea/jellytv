using System;

namespace Jellyfin.Plugin.JellyChannels.Services;

/// <summary>
/// Los ajustes de YouTube, leídos de la configuración del plugin en el momento de usarlos.
/// </summary>
/// <param name="Enabled">Si las fuentes de YouTube están activadas.</param>
/// <param name="Format">Selector de formato de yt-dlp.</param>
/// <param name="MaxCacheBytes">Tamaño máximo de la caché; 0 no limita.</param>
/// <param name="PrefetchMargin">Antelación con la que se descarga cada vídeo.</param>
public sealed record YoutubeSettings(bool Enabled, string Format, long MaxCacheBytes, TimeSpan PrefetchMargin)
{
    /// <summary>
    /// Lee los ajustes actuales del plugin.
    /// </summary>
    /// <returns>Los ajustes.</returns>
    public static YoutubeSettings FromConfiguration()
    {
        var config = Plugin.Instance?.Configuration;
        return new YoutubeSettings(
            config?.EnableYoutube ?? false,
            string.IsNullOrWhiteSpace(config?.YoutubeFormat) ? "b[ext=mp4]/b" : config.YoutubeFormat.Trim(),
            Math.Max(0, config?.YoutubeMaxCacheMb ?? 5120) * 1024L * 1024L,
            TimeSpan.FromMinutes(Math.Clamp(config?.YoutubePrefetchMinutes ?? 30, 1, 24 * 60)));
    }
}
