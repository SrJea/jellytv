using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyChannels.Services;

/// <summary>
/// Lo que hay que hacer tras un repaso de la caché de YouTube.
/// </summary>
/// <param name="MetadataToFetch">Vídeos de los que faltan los metadatos.</param>
/// <param name="ToDownload">Vídeos que hacen falta pronto y no están descargados.</param>
/// <param name="Deleted">Vídeos cuyo fichero se acaba de borrar.</param>
public sealed record YoutubePlan(IReadOnlyList<string> MetadataToFetch, IReadOnlyList<string> ToDownload, IReadOnlyList<string> Deleted);
