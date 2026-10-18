using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.JellyChannels.Services;

/// <summary>
/// Ejecuta yt-dlp.
/// </summary>
public interface IYtDlpRunner
{
    /// <summary>
    /// Ejecuta yt-dlp con los argumentos dados, sin pasar por una shell.
    /// </summary>
    /// <param name="arguments">Argumentos, uno por elemento.</param>
    /// <param name="timeout">Tiempo máximo; pasado ese tiempo se mata el proceso.</param>
    /// <param name="cancellationToken">Cancelación; mata el proceso.</param>
    /// <returns>El resultado.</returns>
    Task<YtDlpResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken);
}
