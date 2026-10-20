namespace FireSquad.Lobby.Models;

public class LobbyPlayer
{
    public string PlayerId { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public PlayerRole? Role { get; set; }

    public bool IsReady { get; set; } = false;

    public bool IsHost { get; set; } = false;

    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}