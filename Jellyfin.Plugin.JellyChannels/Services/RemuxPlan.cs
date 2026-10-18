namespace Jellyfin.Plugin.JellyChannels.Services;

/// <summary>
/// Qué hacer con cada pista de un item: copiarla o recodificarla a H.264 / AAC.
/// Todo el stream sale siempre en H.264 y AAC, así que el reproductor nunca ve un cambio de códec.
/// </summary>
/// <param name="TranscodeVideo">Recodificar el vídeo a H.264.</param>
/// <param name="TranscodeAudio">Recodificar el audio a AAC.</param>
/// <param name="AudioChannels">Si se fija, canales de salida del audio recodificado.</param>
public sealed record RemuxPlan(bool TranscodeVideo, bool TranscodeAudio, int? AudioChannels = null)
{
    /// <summary>
    /// Gets el plan de solo copiar.
    /// </summary>
    public static RemuxPlan Copy { get; } = new(false, false);

    /// <summary>
    /// Gets a value indicating whether alguna pista se recodifica.
    /// </summary>
    public bool NeedsTranscoding => TranscodeVideo || TranscodeAudio;

    private static bool Is420(string? pixelFormat)
        => pixelFormat is null || pixelFormat.StartsWith("yuv420p", System.StringComparison.OrdinalIgnoreCase) || pixelFormat.StartsWith("yuvj420p", System.StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Decide qué se copia y qué se recodifica.
    /// </summary>
    /// <param name="source">El item y sus pistas.</param>
    /// <param name="allowTranscode">False para copiar siempre, como en la v0.5 inicial.</param>
    /// <returns>El plan.</returns>
    public static RemuxPlan Decide(PlaybackSource source, bool allowTranscode)
    {
        System.ArgumentNullException.ThrowIfNull(source);

        if (!allowTranscode)
        {
            return Copy;
        }

        if (!source.StreamsKnown)
        {
            return new RemuxPlan(true, true);
        }

        // H.264 de 8 bits en 4:2:0 y AAC son lo que entienden todos los reproductores de Live TV; el resto
        // se recodifica (incluido H.264 en 4:4:4 o de 10 bits, que casi ningún reproductor decodifica).
        bool video = source.Video is { } v && !(v.Codec == "h264" && (v.BitDepth ?? 8) <= 8 && Is420(v.PixelFormat));
        bool audio = source.Audio is { } a && a.Codec != "aac";

        // El codificador AAC de ffmpeg no admite más de 8 canales.
        int? channels = audio && source.Audio!.Channels > 8 ? 2 : null;
        return new RemuxPlan(video, audio, channels);
    }
}
