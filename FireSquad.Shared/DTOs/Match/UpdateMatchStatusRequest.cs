using FireSquad.Shared.Enums;

namespace FireSquad.Shared.DTOs.Matches;

public sealed class UpdateMatchStatusRequest
{
    public MatchStatus Status { get; set; }
    public string? FailureReason { get; set; }
}
