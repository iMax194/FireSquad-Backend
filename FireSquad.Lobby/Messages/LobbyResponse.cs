
namespace FireSquad.Lobby.Messages;

public class LobbyResponse
{
    public string Type { get; set; } = string.Empty;

    public string? RequestId { get; set; }

    public object? Payload { get; set; }

    public static LobbyResponse Create(
        string type,
        object? payload,
        string? requestId = null)
    {
        return new LobbyResponse
        {
            Type = type,
            RequestId = requestId,
            Payload = payload
        };
    }
}
