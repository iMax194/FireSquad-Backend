namespace FireSquad.Shared.DTOs.Matches;

public sealed class MatchPlayerDto
{
    public string PlayerId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsHost { get; set; }
}
