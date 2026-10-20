namespace FireSquad.Lobby.Models;

public record RoomLeaveResult(
    LobbyRoom? Room,
    bool PlayerRemoved,
    bool StartingCancelled,
    string? CancelledMatchId
);
