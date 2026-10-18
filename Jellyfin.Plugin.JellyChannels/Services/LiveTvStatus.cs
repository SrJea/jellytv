namespace Jellyfin.Plugin.JellyChannels.Services;

/// <summary>
/// Estado del registro de JellyChannels en Live TV.
/// </summary>
/// <param name="AutoRegister">Si el registro automático está activado en los ajustes.</param>
/// <param name="TunerRegistered">Si el tuner M3U de JellyChannels está en Live TV.</param>
/// <param name="ListingsRegistered">Si la guía XMLTV de JellyChannels está en Live TV.</param>
/// <param name="ApiKeyExists">Si existe la clave de API que usa el registro.</param>
public sealed record LiveTvStatus(bool AutoRegister, bool TunerRegistered, bool ListingsRegistered, bool ApiKeyExists);
