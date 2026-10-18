using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.JellyChannels.Services;

/// <summary>
/// Ejecuta ffmpeg para un tramo y vuelca su salida en un stream.
/// </summary>
public interface IFfmpegRunner
{
    /// <summary>
    /// Gets a value indicating whether hay un ffmpeg utilizable.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Remuxa un tramo escribiendo en <paramref name="output"/> hasta que acaba o se cancela.
    /// </summary>
    /// <param name="request">El tramo.</param>
    /// <param name="output">Destino del MPEG-TS.</param>
    /// <param name="cancellationToken">Cancelación; mata el proceso.</param>
    /// <returns>Código de salida y bytes escritos.</returns>
    Task<RemuxResult> RunAsync(RemuxRequest request, Stream output, CancellationToken cancellationToken);
}
