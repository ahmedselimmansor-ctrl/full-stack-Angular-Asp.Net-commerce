using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sama.Shared.Kernel;

/// <summary>
/// The one JSON configuration used for API payloads and for event envelopes.
/// <para>
/// Producers and consumers of an event must agree byte-for-byte on naming and enum
/// handling; sharing a single options instance is what keeps a casing tweak in one service
/// from silently breaking deserialisation in another.
/// </para>
/// </summary>
public static class SamaJson
{
    public static JsonSerializerOptions Default { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,

            // Omitting nulls keeps mobile payloads smaller; clients must treat absent as null.
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

            // Reject unknown fields rather than dropping them silently: on an inbound event
            // an unexpected member usually means a producer moved ahead of this consumer,
            // and failing loudly routes it to the dead-letter queue instead of losing data.
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };

        // Enums travel as their names. Ordinals reshuffle the moment someone inserts a value
        // into the middle of an enum, silently changing the meaning of persisted events.
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

        return options;
    }

    /// <summary>
    /// Lenient variant for inbound API request bodies, where an extra field from a newer
    /// client should not be a 400.
    /// </summary>
    public static JsonSerializerOptions Lenient { get; } = CreateLenient();

    private static JsonSerializerOptions CreateLenient()
    {
        var options = new JsonSerializerOptions(Default)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        };
        return options;
    }
}
