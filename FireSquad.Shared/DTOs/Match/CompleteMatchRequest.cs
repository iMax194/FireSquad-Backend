namespace FireSquad.Shared.DTOs.Matches;

public sealed class CompleteMatchRequest
{
    public string? WinnerTeam { get; set; }
    public string? Summary { get; set; }
    public Dictionary<string, int> Scores { get; set; } = new();
}
