using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyChannels.Services;

/// <summary>
/// Opciones JSON del plugin: camelCase y enums en kebab-case (<c>seeded-shuffle</c>),
/// igual que el modelo de canal documentado.
/// </summary>
public static class ChannelJson
{
    /// <summary>
    /// Gets las opciones compartidas.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
