
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using FireSquad.Lobby.Manager;
using FireSquad.Lobby.Messages;
using FireSquad.Lobby.Models;

namespace FireSquad.Lobby.Networking;

public class LobbyTcpServer
{
    private readonly TcpListener _listener;
    private readonly LobbyManager _manager = new();

    private readonly ConcurrentDictionary<string, ClientConnection> _connections = new();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public LobbyTcpServer(int port = 7777)
    {
        _listener = new TcpListener(IPAddress.Loopback, port);
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _listener.Start();
        Console.WriteLine("[Lobby] Listening on 127.0.0.1:7777");

        try
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient tcpClient;

                try
                {
                    tcpClient = await _listener.AcceptTcpClientAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                ClientConnection connection;

                try
                {
                    connection =new ClientConnection(tcpClient);
                }
                catch
                {
                    tcpClient.Dispose();
                    continue;
                }

                string id = connection.Session.SessionId;

                if (!_connections.TryAdd(id, connection))
                {
                    connection.Dispose();
                    continue;
                }

                Console.WriteLine($"[Lobby] Connected: {id}");

                _ = HandleClientAsync(connection, ct);
            }
        }
        finally
        {
            _listener.Stop();

            foreach (var connection in _connections.Values) connection.Session.Client.Close();
        }
    }

    private async Task HandleClientAsync(ClientConnection connection, CancellationToken ct)
    {
        var session = connection.Session;

        try
        {
            await connection.SendAsync(
                LobbyResponse.Create("WELCOME", new
                {
                    playerId = session.SessionId,
                    message = "Connected to FireSquad Lobby"
                }), ct);

            while (!ct.IsCancellationRequested)
            {
                string? line =
                    await connection.ReadLineAsync(ct);

                if (line == null)
                    break;

                LobbyRequest? request;

                try
                {
                    request = JsonSerializer.Deserialize<LobbyRequest>(line, JsonOptions);
                }
                catch (JsonException)
                {
                    await SendErrorAsync(connection, "Invalid JSON", null, ct);
                    continue;
                }

                if (request == null)
                {
                    await SendErrorAsync(connection, "Empty request", null, ct);
                    continue;
                }

                try
                {
                    await ProcessRequestAsync(connection, request, ct);
                }
                catch (InvalidOperationException ex)
                {
                    await SendErrorAsync(connection, ex.Message, request.RequestId, ct);
                }
                catch (JsonException)
                {
                    await SendErrorAsync(connection, "Invalid payload", request.RequestId, ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Server is stopping.
        }
        catch (IOException)
        {
            Console.WriteLine($"[Lobby] Connection lost: {session.SessionId}");
        }
        catch (SocketException)
        {
            Console.WriteLine(
                $"[Lobby] Socket error: {session.SessionId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[Lobby] Client handler error: {ex.Message}");
        }
        finally
        {
            _connections.TryRemove(session.SessionId, out _);

            string? code = session.RoomCode;

            if (!string.IsNullOrEmpty(code))
            {
                try
                {
                    var room = _manager.LeaveRoom(code, session.SessionId);

                    await BroadcastRoomAsync(room, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Lobby] Cleanup error: {ex.Message}");
                }
            }

            connection.Dispose();

            Console.WriteLine($"[Lobby] Disconnected: {session.SessionId}");
        }
    }

    private async Task ProcessRequestAsync(ClientConnection connection, LobbyRequest request, CancellationToken ct)
    {
        var session = connection.Session;
        var type = request.Type?.ToUpperInvariant();

        switch (type)
        {
            case "CREATE_ROOM":
            {
                if (session.RoomCode != null)
                    throw new InvalidOperationException("Already in a room.");

                var payload = ReadPayload<CreateRoomPayload>(request);

                string username = ValidateUsername(payload.Username);

                var room = _manager.CreateRoom(session.SessionId, username);

                session.Username = username;
                session.RoomCode = room.RoomCode;

                await connection.SendAsync(
                    LobbyResponse.Create(
                        "ROOM_CREATED",
                        new
                        {
                            roomCode = room.RoomCode,
                            playerId = session.SessionId
                        },
                        request.RequestId), ct);

                await BroadcastRoomAsync(room, ct);
                break;
            }

            case "JOIN_ROOM":
            {
                if (session.RoomCode != null)
                    throw new InvalidOperationException("Already in a room.");

                var payload = ReadPayload<JoinRoomPayload>(request);

                string username = ValidateUsername(payload.Username);

                string code = payload.RoomCode.Trim().ToUpperInvariant();

                var room = _manager.JoinRoom(code, session.SessionId, username);

                session.Username = username;
                session.RoomCode = room.RoomCode;

                await connection.SendAsync(
                    LobbyResponse.Create(
                        "ROOM_JOINED",
                        new
                        {
                            roomCode = room.RoomCode,
                            playerId = session.SessionId
                        },
                        request.RequestId), ct);

                await BroadcastRoomAsync(room, ct);
                break;
            }

            case "SELECT_ROLE":
            {
                if (session.RoomCode == null)
                {
                    throw new InvalidOperationException(
                        "You must join a room first.");
                }

                var payload = ReadPayload<SelectRolePayload>(
                    request);

                // Convert role string to PlayerRole enum
                if (!Enum.TryParse<PlayerRole>(
                        payload.Role,
                        ignoreCase: true,
                        out var role)
                    || !Enum.IsDefined(role))
                {
                    throw new InvalidOperationException(
                        "Invalid player role.");
                }

                // LobbyManager validates role availability
                var room = _manager.SelectRole(
                    session.RoomCode,
                    session.SessionId,
                    role);

                // Confirm request to the player
                await connection.SendAsync(
                    LobbyResponse.Create(
                        "ROLE_SELECTED",
                        new
                        {
                            role = role.ToString()
                        },
                        request.RequestId),
                    ct);

                // Update all players in the room
                await BroadcastRoomAsync(room, ct);

                break;
            }
            
            case "SET_READY":
            {
                if (session.RoomCode == null)
                {
                    throw new InvalidOperationException("You must join a room first.");
                }

                var payload = ReadPayload<SetReadyPayload>(request);

                if (payload.Ready == null)
                {
                    throw new InvalidOperationException("Ready field is required.");
                }

                var room = _manager.SetReady(
                    session.RoomCode,
                    session.SessionId,
                    payload.Ready.Value);

                await connection.SendAsync(
                    LobbyResponse.Create(
                        "READY_UPDATED",
                        new
                        {
                            isReady = payload.Ready
                        },
                        request.RequestId),
                    ct);

                await BroadcastRoomAsync(room, ct);

                break;
            }

            case "LEAVE_ROOM":
            {
                if (session.RoomCode == null)
                    throw new InvalidOperationException("Not in a room.");

                string code = session.RoomCode;

                var room = _manager.LeaveRoom(code, session.SessionId);

                session.RoomCode = null;

                await connection.SendAsync(
                    LobbyResponse.Create(
                        "ROOM_LEFT",
                        new { roomCode = code },
                        request.RequestId), ct);

                await BroadcastRoomAsync(room, ct);
                break;
            }

            default:
                await SendErrorAsync(
                    connection, "Unknown request type",
                    request.RequestId, ct);
                break;
        }
    }

    private static T ReadPayload<T>(
        LobbyRequest request)
    {
        if (request.Payload.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Payload must be an object.");
        }

        return request.Payload.Deserialize<T>(JsonOptions)
            ?? throw new JsonException("Missing payload.");
    }

    private static string ValidateUsername(string? username)
    {
        string value = username?.Trim() ?? "";

        if (value.Length < 3 || value.Length > 30)
            throw new InvalidOperationException("Username must be 3-30 characters.");

        return value;
    }

    private async Task BroadcastRoomAsync(
        LobbyRoom? room,
        CancellationToken ct)
    {
        if (room == null)
            return;

        bool canStart = _manager.CanStart(room.RoomCode,room.HostPlayerId);

        var response = LobbyResponse.Create(
            "ROOM_UPDATED", new
            {
                roomCode = room.RoomCode,
                hostPlayerId = room.HostPlayerId,
                state = room.State,
                maxPlayers = room.MaxPlayers,
                canStart = canStart,
                players = room.Players.Select(p => new
                {
                    playerId = p.PlayerId,
                    username = p.Username,
                    role = p.Role?.ToString(),
                    isReady = p.IsReady,
                    isHost = p.IsHost
                }).ToArray()
            });

        foreach (var player in room.Players)
        {
            if (_connections.TryGetValue(player.PlayerId, out var connection))
            {
                try
                {
                    await connection.SendAsync(response, ct);
                }
                catch (IOException)
                {
                    // Disconnected client will be cleaned up.
                }
                catch (ObjectDisposedException)
                {
                    // Connection was closed.
                }
                catch (OperationCanceledException)
                {
                    // Server is stopping.
                }
            }
        }
    }

    private static Task SendErrorAsync(
        ClientConnection connection,
        string message,
        string? requestId,
        CancellationToken ct)
    {
        return connection.SendAsync(LobbyResponse.Create("ERROR", new { message }, requestId), ct);
    }
}
