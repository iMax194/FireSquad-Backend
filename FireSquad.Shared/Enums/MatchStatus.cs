using System.Text.Json.Serialization;

namespace FireSquad.Shared.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MatchStatus
{
    Starting = 1,
    InProgress = 2,
    Completed = 3,
    Aborted = 4,
    Failed = 5
}
