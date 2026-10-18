namespace Jellyfin.Plugin.JellyChannels.Services;

/// <summary>
/// Lo que el remux necesita saber de una pista para decidir si se copia o se recodifica.
/// </summary>
/// <param name="Codec">Nombre del códec en minúsculas (<c>h264</c>, <c>aac</c>, <c>hevc</c>...).</param>
/// <param name="BitDepth">Profundidad de bits del vídeo, si se conoce.</param>
/// <param name="Channels">Canales de audio, si se conocen.</param>
/// <param name="PixelFormat">Formato de píxel del vídeo (<c>yuv420p</c>, <c>yuv444p</c>...), si se conoce.</param>
public sealed record StreamInfo(string? Codec, int? BitDepth = null, int? Channels = null, string? PixelFormat = null);
