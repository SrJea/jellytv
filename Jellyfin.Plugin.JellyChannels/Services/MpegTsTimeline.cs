using System;

namespace Jellyfin.Plugin.JellyChannels.Services;

/// <summary>
/// Sigue el PTS más alto de un flujo MPEG-TS a medida que pasa. Con <c>-c copy</c> un tramo puede
/// contener más tiempo del que tarda en emitirse (el contenido anterior al salto de <c>-ss</c> sale de
/// golpe), así que el reloj no sirve para saber dónde acaba y hay que leerlo del propio flujo.
/// </summary>
internal sealed class MpegTsTimeline
{
    private const int PacketSize = 188;
    private const byte SyncByte = 0x47;
    private const long TicksPerSecond = 90_000;

    private readonly byte[] _pending = new byte[PacketSize];
    private int _pendingLength;
    private bool _synced = true;
    private long _maxPts = -1;

    /// <summary>
    /// Gets el PTS más alto visto, o null si aún no ha pasado ningún paquete con PTS.
    /// </summary>
    public TimeSpan? Last => _maxPts < 0 ? null : TimeSpan.FromSeconds((double)_maxPts / TicksPerSecond);

    /// <summary>
    /// Procesa un trozo del flujo. Admite cortes en mitad de un paquete.
    /// </summary>
    /// <param name="data">Bytes del flujo, en el orden en que se escriben.</param>
    public void Feed(ReadOnlySpan<byte> data)
    {
        if (!_synced)
        {
            return;
        }

        while (!data.IsEmpty)
        {
            if (_pendingLength > 0 || data.Length < PacketSize)
            {
                int take = Math.Min(PacketSize - _pendingLength, data.Length);
                data[..take].CopyTo(_pending.AsSpan(_pendingLength));
                _pendingLength += take;
                data = data[take..];
                if (_pendingLength < PacketSize)
                {
                    return;
                }

                _pendingLength = 0;
                if (!Inspect(_pending))
                {
                    return;
                }
            }
            else
            {
                if (!Inspect(data[..PacketSize]))
                {
                    return;
                }

                data = data[PacketSize..];
            }
        }
    }

    /// <summary>
    /// Mira un paquete TS. Devuelve false si el flujo perdió la sincronización (deja de seguirse).
    /// </summary>
    private bool Inspect(ReadOnlySpan<byte> packet)
    {
        if (packet[0] != SyncByte)
        {
            _synced = false;
            return false;
        }

        bool payloadStart = (packet[1] & 0x40) != 0;
        int adaptation = (packet[3] >> 4) & 0x3;
        if (!payloadStart || (adaptation & 0x1) == 0)
        {
            return true;
        }

        int offset = 4;
        if ((adaptation & 0x2) != 0)
        {
            offset += 1 + packet[4];
        }

        // Cabecera PES: 00 00 01 <stream_id> <len:2> <flags:2> <header_len> <PTS:5>.
        if (offset + 14 > PacketSize || packet[offset] != 0 || packet[offset + 1] != 0 || packet[offset + 2] != 1)
        {
            return true;
        }

        byte streamId = packet[offset + 3];
        bool audioOrVideo = streamId is (>= 0xC0 and <= 0xEF) or 0xBD;
        bool hasPts = (packet[offset + 7] & 0x80) != 0;
        if (!audioOrVideo || !hasPts)
        {
            return true;
        }

        var p = packet[(offset + 9)..];
        long pts = ((long)((p[0] >> 1) & 0x07) << 30)
            | ((long)p[1] << 22)
            | ((long)(p[2] >> 1) << 15)
            | ((long)p[3] << 7)
            | (long)(p[4] >> 1);
        if (pts > _maxPts)
        {
            _maxPts = pts;
        }

        return true;
    }
}
