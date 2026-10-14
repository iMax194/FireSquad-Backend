
namespace FireSquad.Lobby.Models;

public class LobbyRoom
{
    public string RoomCode { get; set; } = string.Empty;

    public string HostPlayerId { get; set; } = string.Empty;

    public int MaxPlayers { get; set; } = 4;

    public LobbyRoomState State { get; set; } = LobbyRoomState.Waiting;

    public List<LobbyPlayer> Players { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string? MatchId { get; set; }

    public DateTime? StartedAt { get; set; }
}
