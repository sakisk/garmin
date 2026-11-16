using System.Text.Json.Serialization;

namespace Garmin.PersonalApi;

/// <summary>A supported subset of Garmin's ActivitySummaryDTO. Absent fields remain null.</summary>
public sealed class Activity
{
    [JsonPropertyName("activityId")]
    public long? ActivityId { get; init; }
    [JsonPropertyName("activityName")]
    public string? ActivityName { get; init; }
    [JsonPropertyName("activityTypeDTO")]
    public ActivityType? ActivityType { get; init; }
    [JsonPropertyName("summaryDTO")]
    public ActivitySummary? Summary { get; init; }
}

public sealed class ActivityType
{
    [JsonPropertyName("typeId")]
    public long? TypeId { get; init; }
    [JsonPropertyName("typeKey")]
    public string? TypeKey { get; init; }
}

public sealed class ActivitySummary
{
    /// <summary>UTC despite Garmin omitting the offset suffix. Kind can be Unspecified.</summary>
    [JsonPropertyName("startTimeGMT")]
    public DateTime? StartTimeGmt { get; init; }
    /// <summary>Activity-local clock time; no time zone or offset is supplied.</summary>
    [JsonPropertyName("startTimeLocal")]
    public DateTime? StartTimeLocal { get; init; }
    /// <summary>Distance in metres.</summary>
    [JsonPropertyName("distance")]
    public double? Distance { get; init; }
    /// <summary>Duration in seconds.</summary>
    [JsonPropertyName("duration")]
    public double? Duration { get; init; }
}
