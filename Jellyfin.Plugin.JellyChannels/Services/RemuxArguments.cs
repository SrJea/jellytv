using System;
using System.Collections.Generic;
using System.Globalization;

namespace Jellyfin.Plugin.JellyChannels.Services;

/// <summary>
/// Argumentos de ffmpeg para remuxar un item a MPEG-TS, copiando las pistas ya compatibles y
/// recodificando solo las que no lo son.
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

        var plan = request.Plan ?? RemuxPlan.Copy;
        args.AddRange(["-map", "0:v:0?", "-map", "0:a:0?"]);

        if (plan.TranscodeVideo)
        {
            // Keyframes cada 2 s para que quien entre a mitad de stream no espere. Sin fotogramas B el DTS
            // coincide con el PTS, y el tramo siguiente no puede empezar con un DTS anterior al final del actual.
            args.AddRange(
            [
                "-c:v", "libx264",
                "-preset", "veryfast",
                "-crf", "23",
                "-pix_fmt", "yuv420p",
                "-bf", "0",
                "-force_key_frames", "expr:gte(t,n_forced*2)"
            ]);
        }
        else
        {
            args.AddRange(["-c:v", "copy"]);
        }

        if (plan.TranscodeAudio)
        {
            args.AddRange(["-c:a", "aac", "-b:a", "192k"]);
            if (plan.AudioChannels is int channels)
            {
                args.AddRange(["-ac", channels.ToString(CultureInfo.InvariantCulture)]);
            }
        }
        else
        {
            args.AddRange(["-c:a", "copy"]);
        }

        args.AddRange(
        [
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
