
using System.Net.Sockets;

namespace FireSquad.Lobby.Models;

public class PlayerSession
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");

    public string? UserId { get; set; }

    public string? Username { get; set; }

    public string? RoomCode { get; set; }

    public TcpClient Client { get; }

    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;

    public PlayerSession(TcpClient client)
    {
        Client = client;
    }
}
