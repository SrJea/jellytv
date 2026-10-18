using System;

namespace Jellyfin.Plugin.JellyChannels.Services;

/// <summary>
/// Un tramo del stream continuo: un item, desde un offset, hasta como mucho el final de su hueco.
/// </summary>
/// <param name="Path">Fichero de entrada.</param>
/// <param name="Offset">Desde dónde se empieza dentro del fichero.</param>
/// <param name="MaxDuration">Duración máxima del tramo, o cero para no limitarla.</param>
/// <param name="TimestampOffset">Instante de la línea de tiempo del stream en que empieza el tramo.</param>
public sealed record RemuxRequest(string Path, TimeSpan Offset, TimeSpan MaxDuration, TimeSpan TimestampOffset);
