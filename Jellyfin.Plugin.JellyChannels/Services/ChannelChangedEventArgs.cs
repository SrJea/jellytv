using System;

namespace Jellyfin.Plugin.JellyChannels.Services;

/// <summary>
/// Datos del evento <see cref="ChannelStore.Changed"/>.
/// </summary>
public class ChannelChangedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChannelChangedEventArgs"/> class.
    /// </summary>
    /// <param name="channelId">Id del canal.</param>
    public ChannelChangedEventArgs(string channelId)
    {
        ChannelId = channelId;
    }

    /// <summary>
    /// Gets el id del canal que ha cambiado.
    /// </summary>
    public string ChannelId { get; }
}
