namespace Jellyfin.Plugin.JellyChannels.Services;

/// <summary>
/// Un item listo para emitir: fichero en disco y códecs de sus pistas.
/// </summary>
/// <param name="Path">Fichero local.</param>
/// <param name="Video">Primera pista de vídeo, o null si no tiene.</param>
/// <param name="Audio">Primera pista de audio, o null si no tiene.</param>
/// <param name="StreamsKnown">False si no se pudieron leer las pistas; entonces no se puede asegurar que se pueda copiar.</param>
public sealed record PlaybackSource(string Path, StreamInfo? Video, StreamInfo? Audio, bool StreamsKnown = true);
