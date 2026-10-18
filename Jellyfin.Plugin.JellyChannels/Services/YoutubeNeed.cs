using System;

namespace Jellyfin.Plugin.JellyChannels.Services;

/// <summary>
/// Un hueco de la parrilla, dentro de la ventana de descarga, que emite un vídeo.
/// </summary>
/// <param name="VideoId">Id de YouTube.</param>
/// <param name="Start">Inicio del hueco.</param>
/// <param name="End">Fin del hueco.</param>
public sealed record YoutubeNeed(string VideoId, DateTimeOffset Start, DateTimeOffset End);
