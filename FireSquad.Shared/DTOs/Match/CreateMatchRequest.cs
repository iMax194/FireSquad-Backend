namespace FireSquad.Shared.DTOs.Matches;

public sealed class CreateMatchRequest
{
    public string MatchId { get; set; } = string.Empty;
    public string RoomCode { get; set; } = string.Empty;
    public string HostUserId { get; set; } = string.Empty;
    public List<MatchPlayerDto> Players { get; set; } = new();
    public DateTime StartingAt { get; set; }
}
