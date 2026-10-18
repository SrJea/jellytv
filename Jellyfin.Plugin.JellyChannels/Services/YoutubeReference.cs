using System;

namespace Jellyfin.Plugin.JellyChannels.Services;

/// <summary>
/// Un vídeo que una fuente de algún canal usa, con su política de retención.
/// </summary>
/// <param name="VideoId">Id de YouTube.</param>
/// <param name="Retention">Política de la fuente.</param>
public sealed record YoutubeReference(string VideoId, YoutubeRetention Retention);
