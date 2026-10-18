using System;
using System.Collections.Generic;
using System.Globalization;

namespace Jellyfin.Plugin.JellyChannels.Services;

/// <summary>
/// Argumentos de ffmpeg para remuxar un item a MPEG-TS sin recodificar.
/// </summary>
internal static class RemuxArguments
{
    /// <summary>
    /// Construye los argumentos.
    /// </summary>
    /// <param name="request">El tramo.</param>
    /// <returns>Argumentos para <c>ProcessStartInfo.ArgumentList</c>, sin pasar por una shell.</returns>
    public static IReadOnlyList<string> Build(RemuxRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin", "-re" };

        // -ss antes de -i con copia salta al keyframe anterior: el tramo puede empezar hasta un GOP antes.
        var seek = Seconds(request.Offset);
        if (request.Offset > TimeSpan.Zero && seek != "0.000")
        {
            args.AddRange(["-ss", seek]);
        }

        args.AddRange(["-i", request.Path]);

        if (request.MaxDuration > TimeSpan.Zero)
        {
            args.AddRange(["-t", Seconds(request.MaxDuration)]);
        }

        args.AddRange(
        [
            "-map", "0:v:0?",
            "-map", "0:a:0?",
            "-c", "copy",
            "-muxdelay", "0",
            "-muxpreload", "0",
            "-flush_packets", "1",

            // El discontinuity indicator avisa al reproductor del salto entre ficheros.
            "-mpegts_flags", "+resend_headers+initial_discontinuity",
            "-output_ts_offset", Seconds(request.TimestampOffset),
            "-f", "mpegts",
            "pipe:1"
        ]);
        return args;
    }

    private static string Seconds(TimeSpan time)
        => time.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture);
}
