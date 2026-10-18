namespace Jellyfin.Plugin.JellyChannels.Services;

/// <summary>
/// Resultado de una ejecución de yt-dlp.
/// </summary>
/// <param name="ExitCode">Código de salida; -1 si no se pudo lanzar o se agotó el tiempo.</param>
/// <param name="StdOut">Salida estándar.</param>
/// <param name="StdErr">Salida de error.</param>
public sealed record YtDlpResult(int ExitCode, string StdOut, string StdErr)
{
    /// <summary>
    /// Gets a value indicating whether terminó bien.
    /// </summary>
    public bool Succeeded => ExitCode == 0;

    /// <summary>
    /// Devuelve la última línea no vacía de un texto, que en yt-dlp es el motivo del error.
    /// </summary>
    /// <returns>La línea, o un texto genérico.</returns>
    public string LastErrorLine()
    {
        var lines = (string.IsNullOrWhiteSpace(StdErr) ? StdOut : StdErr).Split('\n', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
        return lines.Length == 0 ? $"yt-dlp terminó con código {ExitCode}" : lines[^1];
    }
}
