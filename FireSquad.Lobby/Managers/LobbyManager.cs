using System.Security.Cryptography;
using FireSquad.Lobby.Models;

namespace FireSquad.Lobby.Manager;

public class LobbyManager
{
    private readonly Dictionary<string, LobbyRoom> _rooms = new(StringComparer.OrdinalIgnoreCase);

    private readonly object _lock = new();

    private const string RoomCharacters = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private static LobbyRoom Snapshot(LobbyRoom room)
    {
        return new LobbyRoom
        {
            RoomCode = room.RoomCode,
            HostPlayerId = room.HostPlayerId,
            MaxPlayers = room.MaxPlayers,
            State = room.State,
            CreatedAt = room.CreatedAt,
            MatchId = room.MatchId,
            StartedAt = room.StartedAt,
            RoomVersion = room.RoomVersion,
            Players = room.Players.Select(p => new LobbyPlayer
            {
                PlayerId = p.PlayerId,
                    Username = p.Username,
                    Role = p.Role,
                    IsReady = p.IsReady,
                    IsHost = p.IsHost,
                    JoinedAt = p.JoinedAt
            }).ToList()
        };
    }

    private static string GenerateRoomCode()
    {
        return new string(
            Enumerable.Range(0,6).Select(
                _ => RoomCharacters[
                    RandomNumberGenerator.GetInt32(
                        RoomCharacters.Length
                    )
                ]
            ).ToArray()
        );
    }

    public LobbyRoom CreateRoom(string playerId, string username)
    {
        lock (_lock)
        {
            if (IsPlayerInRoom(playerId))
            {
                throw new InvalidOperationException("Player is already in a room");
            }

            string code;

            do
            {
                code = GenerateRoomCode();
            } while (_rooms.ContainsKey(code));

            var room = new LobbyRoom
            {
                RoomCode = code,
                HostPlayerId = playerId  
            };

            room.Players.Add(new LobbyPlayer
            {
                PlayerId = playerId,
                Username = username,
                IsHost = true
            });

            _rooms.Add(code, room);

            room.RoomVersion++;

            return Snapshot(room);
        }
    }

    public LobbyRoom JoinRoom(string code, string playerId, string username)
    {
        lock (_lock)
        {
            if (IsPlayerInRoom(playerId))
            {
                throw new InvalidOperationException("Player is already in a room.");
            }

            if (!_rooms.TryGetValue(code, out var room))
            {
                throw new InvalidOperationException("Room not found.");
            }

            if (room.State != LobbyRoomState.Waiting)
            {
                throw new InvalidOperationException("Room is not accepting players.");
            }

            if (room.Players.Count > room.MaxPlayers)
            {
                throw new InvalidOperationException("Room is full.");
            }

            room.Players.Add(new LobbyPlayer
            {
                PlayerId = playerId,
                Username = username
            });

            room.RoomVersion++;

            return Snapshot(room);
        }
    }

    public LobbyRoom SelectRole(string code, string playerId, PlayerRole role)
    {
        lock (_lock)
        {
            var room = GetWaitingRoom(code);
            var player = GetPlayer(room, playerId);

            if (!Enum.IsDefined(role))
            {
                throw new InvalidOperationException("Invalid role.");
            }

            if (room.Players.Any(p => p.PlayerId != playerId && p.Role == role))
            {
                throw new InvalidOperationException("Role is already taken.");
            }

            player.Role = role;
            player.IsReady = false;
            room.RoomVersion++;

            return Snapshot(room);
        }
    }

    public LobbyRoom SetReady(string code, string playerId, bool ready)
    {
        lock (_lock)
        {
            var room = GetWaitingRoom(code);
            var player = GetPlayer(room, playerId);

            if (ready && player.Role == null)
            {
                throw new InvalidOperationException("Please select a role first.");
            }

            player.IsReady = ready;
            room.RoomVersion++;

            return Snapshot(room);
        }
    }

    public bool CanStart(string code, string playerId)
    {
        lock (_lock)
        {
            if (!_rooms.TryGetValue(code, out var room)) return false;

            return room.State == LobbyRoomState.Waiting
                && room.HostPlayerId == playerId
                && room.Players.Count == room.MaxPlayers
                && room.Players.All(p => p.Role.HasValue && p.IsReady)
                && room.Players.Select(p => p.Role).Distinct().Count() == room.MaxPlayers;
        }
    }

    public LobbyRoom StarGame(string roomCode, string playerId)
    {
        lock (_lock)
        {
            if (!_rooms.TryGetValue(roomCode, out var room))
            {
                throw new InvalidOperationException("Room not found.");
            }

            if (room.State != LobbyRoomState.Waiting)
            {
                throw new InvalidOperationException("Room is not in Waiting state.");
            }

            if (room.HostPlayerId != playerId)
            {
                throw new InvalidOperationException("Only the host can start the game.");
            }

            if (room.Players.Count != room.MaxPlayers)
            {
                throw new InvalidOperationException("Four players are required.");
            }

            if (room.Players.Any(p => !p.Role.HasValue))
            {
                throw new InvalidOperationException("All players must select a role.");
            }

            if (room.Players.Any(p => !p.IsReady))
            {
                throw new InvalidOperationException("All players must be ready.");
            }

            int uniqueRole = room.Players.Select(p => p.Role).Distinct().Count();

            if (uniqueRole != room.MaxPlayers)
            {
                throw new InvalidOperationException("Players must have unique roles.");
            }

            // Match Id for server integration
            room.MatchId = Guid.NewGuid().ToString("N");

            room.StartedAt = DateTime.UtcNow;

            room.State = LobbyRoomState.Starting;
            room.RoomVersion++;

            return Snapshot(room);
        }
    }

    
    public RoomLeaveResult LeaveRoomAtomic(
        string code,
        string playerId)
    {
        lock (_lock)
        {
            if (!_rooms.TryGetValue(code, out var room))
            {
                return new RoomLeaveResult(null, false, false, null);
            }

            var player = room.Players.FirstOrDefault(p => p.PlayerId == playerId);

            if (player == null)
            {
                return new RoomLeaveResult(Snapshot(room), false, false, null);
            }

            if (room.State == LobbyRoomState.InGame)
            {
                throw new InvalidOperationException("InGame disconnect belongs to Game Server.");
            }

            bool cancelled = room.State == LobbyRoomState.Starting;

            string? cancelledMatchId = cancelled ? room.MatchId : null;

            if (cancelled)
            {
                room.State = LobbyRoomState.Waiting;
                room.MatchId = null;
                room.StartedAt = null;
            }

            room.Players.Remove(player);

            if (room.Players.Count == 0)
            {
                room.State = LobbyRoomState.Closed;
                _rooms.Remove(code);

                return new RoomLeaveResult(null, true, cancelled, cancelledMatchId);
            }

            if (room.HostPlayerId == playerId)
            {
                room.HostPlayerId = room.Players[0].PlayerId;
            }

            foreach (var p in room.Players)
            {
                p.IsHost = p.PlayerId == room.HostPlayerId;

                if (cancelled)
                {
                    p.IsReady = false;
                }
            }

            room.RoomVersion++;

            return new RoomLeaveResult(Snapshot(room), true, cancelled, cancelledMatchId);
        }
    }


    public LobbyRoom? RollBackStarting(string roomCode, string expectedMatchId)
    {
        lock (_lock)
        {
            if (!_rooms.TryGetValue(roomCode, out var room))
            {
                return null;
            }

            if (room.State != LobbyRoomState.Starting || room.MatchId != expectedMatchId)
            {
                return null;
            }

            Console.WriteLine($"[Lobby] Rolling back match {expectedMatchId}");

            room.State = LobbyRoomState.Waiting;
            room.MatchId = null;
            room.StartedAt = null;
            room.RoomVersion++;

            return Snapshot(room);
        }
    }

    public LobbyRoom? GetRoom(string code)
    {
        lock (_lock)
        {
            return _rooms.TryGetValue(code, out var room)
                ? Snapshot(room)
                : null;
        }
    }

    private bool IsPlayerInRoom(string playerId)
    {
        return _rooms.Values.Any(room =>
            room.Players.Any(p =>
                p.PlayerId == playerId));
    }

    private LobbyRoom GetWaitingRoom(string code)
    {
        if (!_rooms.TryGetValue(code, out var room))
        {
            throw new InvalidOperationException("Room not found.");
        }

        if (room.State != LobbyRoomState.Waiting)
        {
            throw new InvalidOperationException("Room is not in Waiting state.");
        }

        return room;
    }

    private static LobbyPlayer GetPlayer(LobbyRoom room, string playerId)
    {
        return room.Players.FirstOrDefault(p => p.PlayerId == playerId)
            ?? throw new InvalidOperationException("Player is not in this room.");
    }

    private static void ResetReady(LobbyRoom room)
    {
        foreach (var player in room.Players)
        {
            player.IsReady = false;
        }
    }
}