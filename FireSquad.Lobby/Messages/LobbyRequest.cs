
using System.Text.Json;

namespace FireSquad.Lobby.Messages;

public class LobbyRequest
{
    public string Type { get; set; } = string.Empty;

    public string? RequestId { get; set; }

    public JsonElement Payload { get; set; }
}

public class CreateRoomPayload
{
    public string Username { get; set; } = string.Empty;
}

public class JoinRoomPayload
{
    public string RoomCode { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;
}

public class LeaveRoomPayload
{
    public string? RoomCode { get; set; }
}


public class SelectRolePayload
{
    public string Role { get; set; } = string.Empty;
}

public class SetReadyPayload
{
    public bool? Ready { get; set; }
}
