namespace FireSquad.Api.DTOs.Matches;

public sealed class MatchHistoryItemResponse
{
    public string MatchId { get; set; } = string.Empty;
    public string RoomCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime StartingAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public long? DurationSeconds { get; set; }
    public List<MatchPlayerResponse> Players { get; set; } = new();
    public MatchResultResponse? Result { get; set; }
}

public sealed class MatchPlayerResponse
{
    public string UserId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsHost { get; set; }
}

public sealed class MatchResultResponse
{
    public string? WinnerTeam { get; set; }
    public string? Summary { get; set; }
    public Dictionary<string, int> Scores { get; set; } = new();
}
