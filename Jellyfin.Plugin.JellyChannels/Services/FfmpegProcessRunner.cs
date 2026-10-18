using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.MediaEncoding;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyChannels.Services;

/// <summary>
/// Lanza el ffmpeg que trae Jellyfin como proceso hijo.
/// </summary>
public class FfmpegProcessRunner : IFfmpegRunner
{
    private const int BufferSize = 64 * 1024;
    private const int MaxStderrChars = 2000;

    private readonly IMediaEncoder _mediaEncoder;
    private readonly ILogger<FfmpegProcessRunner> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FfmpegProcessRunner"/> class.
    /// </summary>
    /// <param name="mediaEncoder">Codificador de Jellyfin, que sabe dónde está ffmpeg.</param>
    /// <param name="logger">Logger.</param>
    public FfmpegProcessRunner(IMediaEncoder mediaEncoder, ILogger<FfmpegProcessRunner> logger)
    {
        _mediaEncoder = mediaEncoder;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsAvailable => !string.IsNullOrEmpty(_mediaEncoder.EncoderPath);

    /// <inheritdoc />
    public async Task<RemuxResult> RunAsync(RemuxRequest request, Stream output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(output);

        var startInfo = new ProcessStartInfo(_mediaEncoder.EncoderPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in RemuxArguments.Build(request))
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        try
        {
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        {
            _logger.LogDebug(ex, "No se pudo bajar la prioridad de ffmpeg");
        }

        process.StandardInput.Close();
        var stderrTask = ReadStderrAsync(process);

        long written = 0;
        var timeline = new MpegTsTimeline();
        try
        {
            var buffer = new byte[BufferSize];
            var stdout = process.StandardOutput.BaseStream;
            int read;
            while ((read = await stdout.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                timeline.Feed(buffer.AsSpan(0, read));
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                written += read;
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Cancelación (el cliente se fue) o error de escritura: no se deja ffmpeg huérfano.
            Kill(process);
            throw;
        }

        var stderr = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0 || written == 0)
        {
            _logger.LogWarning("ffmpeg terminó con código {Code} y {Bytes} bytes para {Path}: {Error}", process.ExitCode, written, request.Path, stderr);
        }

        return new RemuxResult(process.ExitCode, written, timeline.Last);
    }

    private static async Task<string> ReadStderrAsync(Process process)
    {
        var text = new StringBuilder();
        string? line;
        while ((line = await process.StandardError.ReadLineAsync().ConfigureAwait(false)) is not null)
        {
            if (text.Length < MaxStderrChars)
            {
                text.AppendLine(line);
            }
        }

        return text.ToString().Trim();
    }

    private void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.LogDebug(ex, "ffmpeg ya había terminado");
        }
    }
}
