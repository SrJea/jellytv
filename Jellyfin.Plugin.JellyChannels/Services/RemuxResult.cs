using System;

namespace Jellyfin.Plugin.JellyChannels.Services;

/// <summary>
/// Resultado de un tramo.
/// </summary>
/// <param name="ExitCode">Código de salida del proceso.</param>
/// <param name="BytesWritten">Bytes escritos en la salida.</param>
/// <param name="LastTimestamp">PTS más alto escrito, en la línea de tiempo del stream, o null si no se pudo leer.</param>
public sealed record RemuxResult(int ExitCode, long BytesWritten, TimeSpan? LastTimestamp = null);
