using System.Text.Json.Serialization;

namespace Garmin.PersonalApi;

[JsonSerializable(typeof(Activity))]
[JsonSerializable(typeof(TokenClaims))]
internal partial class ApiJsonContext : JsonSerializerContext;

internal sealed class TokenClaims
{
    [JsonPropertyName("exp")]
    public long? ExpiresAt { get; init; }
}
