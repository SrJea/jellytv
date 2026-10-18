using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyChannels.Services;

/// <summary>
/// Lanza el binario de yt-dlp configurado en los ajustes como proceso hijo, con prioridad baja.
/// </summary>
public class YtDlpProcessRunner : IYtDlpRunner
{
    private readonly ILogger<YtDlpProcessRunner> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="YtDlpProcessRunner"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    public YtDlpProcessRunner(ILogger<YtDlpProcessRunner> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<YtDlpResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var path = Plugin.Instance?.Configuration.YtDlpPath;
        var startInfo = new ProcessStartInfo(string.IsNullOrWhiteSpace(path) ? "yt-dlp" : path.Trim())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            _logger.LogError(ex, "No se pudo lanzar yt-dlp ({Path}); revisa la ruta en los ajustes del plugin", startInfo.FileName);
            return new YtDlpResult(-1, string.Empty, "No se pudo lanzar yt-dlp: " + ex.Message);
        }

        try
        {
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or PlatformNotSupportedException)
        {
            _logger.LogDebug(ex, "No se pudo bajar la prioridad de yt-dlp");
        }

        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);

        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return new YtDlpResult(-1, string.Empty, $"yt-dlp superó el tiempo máximo ({timeout.TotalMinutes:0} min)");
        }

        return new YtDlpResult(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
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
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            _logger.LogDebug(ex, "yt-dlp ya había terminado");
        }
    }
}
