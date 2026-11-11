
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using FireSquad.Lobby.Manager;
using FireSquad.Lobby.Messages;
using FireSquad.Lobby.Models;
using FireSquad.Lobby.Security;
using FireSquad.Lobby.Services;
using FireSquad.Shared.DTOs.Matches;
using FireSquad.Shared.Enums;

namespace FireSquad.Lobby.Networking;

public class LobbyTcpServer
{
    private readonly TcpListener _listener;
    private readonly LobbyManager _manager = new();
    private readonly JwtValidator _jwtValidator;
    private readonly MatchApiClient _matchApiClient;
    private readonly string _gameServerHost;
    private readonly int _gameServerPort;

    private readonly ConcurrentDictionary<string, ClientConnection> _connections = new();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public LobbyTcpServer(
        int port,
        JwtValidator jwtValidator,
        MatchApiClient matchApiClient,
        string gameServerHost = "127.0.0.1",
        int gameServerPort = 9050)
    {
        if (string.IsNullOrWhiteSpace(gameServerHost))
            throw new ArgumentException("Game Server host is required.", nameof(gameServerHost));

        if (gameServerPort < 1 || gameServerPort > 65535)
            throw new ArgumentOutOfRangeException(nameof(gameServerPort));

        _listener = new TcpListener(IPAddress.Loopback, port);
        _jwtValidator = jwtValidator;
        _matchApiClient = matchApiClient;
        _gameServerHost = gameServerHost.Trim();
        _gameServerPort = gameServerPort;
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

            string? roomCode = session.RoomCode;

            if (!string.IsNullOrWhiteSpace(roomCode))
            {
                try
                {
                    var leaveResult = _manager.LeaveRoomAtomic(roomCode, session.SessionId);

                    bool wasStarting = leaveResult.StartingCancelled;

                    var updatedRoom =leaveResult.Room;

                    string? cancelledMatchId = leaveResult.CancelledMatchId;

                    session.RoomCode = null;

                    if (wasStarting && !string.IsNullOrWhiteSpace(cancelledMatchId))
                    {
                        await TryUpdateMatchStatusAsync(
                            cancelledMatchId,
                            MatchStatus.Aborted,
                            "A player disconnected during Starting.",
                            CancellationToken.None);
                    }

                    if (wasStarting && updatedRoom?.State == LobbyRoomState.Waiting)
                    {
                        var response = LobbyResponse.Create("START_FAILED", new
                        {
                            roomCode,
                            reason = "A player disconnected during Starting."
                        });

                        foreach (var player in updatedRoom.Players)
                        {
                            if (_connections.TryGetValue(
                                player.PlayerId,
                                out var otherConnection))
                            {
                                try
                                {
                                    await otherConnection.SendAsync(
                                        response,
                                        CancellationToken.None);
                                }
                                catch (IOException) { }
                                catch (ObjectDisposedException) { }
                            }
                        }
                    }

                    await BroadcastRoomAsync(updatedRoom, CancellationToken.None);
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
        if (type != "AUTHENTICATE" && string.IsNullOrWhiteSpace(session.UserId))
        {
            await SendErrorAsync(connection, "Authentication required.", request.RequestId, ct);

            return;
        }

        switch (type)
        {
            
            case "AUTHENTICATE":
            {
                if (session.UserId != null)
                {
                    throw new InvalidOperationException("Session is already authenticated.");
                }

                var payload = ReadPayload<AuthenticatePayload>(request);

                if (string.IsNullOrWhiteSpace(payload.Token) || payload.Token.Length > 8192)
                {
                    throw new InvalidOperationException("Invalid token.");
                }

                var identity = _jwtValidator.Validate(payload.Token);

                if (identity == null)
                {
                    await SendErrorAsync(
                        connection,
                        "Invalid or expired token.",
                        request.RequestId,
                        ct);

                    break;
                }

                session.UserId = identity.Value.UserId;
                session.Username = identity.Value.Username;

                await connection.SendAsync(
                    LobbyResponse.Create(
                        "AUTH_SUCCESS",
                        new
                        {
                            userId = session.UserId,
                            username = session.Username,
                            playerId = session.SessionId
                        }, request.RequestId), ct);

                Console.WriteLine($"[Lobby] Authenticated session: {session.SessionId}");

                break;
            }

            case "CREATE_ROOM":
            {
                if (session.RoomCode != null)
                    throw new InvalidOperationException("Already in a room.");

                string username = session.Username ?? throw new InvalidOperationException("Authentication required.");

                var room = _manager.CreateRoom(session.SessionId, username, session.UserId);

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
                if (session.RoomCode != null) throw new InvalidOperationException("Already in a room.");

                var payload = ReadPayload<JoinRoomPayload>(request);

                string username = session.Username ?? throw new InvalidOperationException("Authentication required.");

                string code = payload.RoomCode?.Trim().ToUpperInvariant() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(code))
                {
                    throw new InvalidOperationException("Room code is required.");
                }

                var room = _manager.JoinRoom(code, session.SessionId, username, session.UserId);

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

                bool ready = payload.Ready.Value;

                var room = _manager.SetReady(
                    session.RoomCode,
                    session.SessionId,
                    ready);

                await connection.SendAsync(
                    LobbyResponse.Create(
                        "READY_UPDATED",
                        new
                        {
                            isReady = ready
                        },
                        request.RequestId),
                    ct);

                await BroadcastRoomAsync(room, ct);

                break;
            }

            case "START_GAME":
            {
                if (session.RoomCode == null)
                    throw new InvalidOperationException("Not in a room.");

                var room = _manager.StarGame(session.RoomCode, session.SessionId);
                var matchId = room.MatchId!;
                var host = room.Players.First(p => p.IsHost);

                var createRequest = new CreateMatchRequest
                {
                    MatchId = matchId,
                    RoomCode = room.RoomCode,
                    HostUserId = host.UserId,
                    StartingAt = room.StartedAt ?? DateTime.UtcNow,
                    Players = room.Players.Select(p => new MatchPlayerDto
                    {
                        PlayerId = p.PlayerId,
                        UserId = p.UserId,
                        Username = p.Username,
                        Role = p.Role?.ToString() ?? string.Empty,
                        IsHost = p.IsHost
                    }).ToList()
                };

                try
                {
                    // Persist the match before notifying Unity clients that it is starting.
                    await _matchApiClient.CreateMatchAsync(createRequest, ct);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    Console.WriteLine($"[Lobby] Could not persist match {matchId}: {ex.Message}");
                    var rolledBack = _manager.RollBackStarting(room.RoomCode, matchId);

                    // If the API saved the document but its response was lost, mark it Aborted.
                    await TryUpdateMatchStatusAsync(
                        matchId, MatchStatus.Aborted,
                        "Lobby could not confirm match persistence.", ct);

                    if (rolledBack != null)
                    {
                        await BroadcastStartFailedAsync(
                            rolledBack, matchId,
                            "Could not save match data. Please try again.",
                            request.RequestId, ct);
                        await BroadcastRoomAsync(rolledBack, ct);
                    }
                    else
                    {
                        await SendErrorAsync(
                            connection, "Could not save match data. Please try again.",
                            request.RequestId, ct);
                    }

                    break;
                }

                // A player may have left while the HTTP request was in flight.
                var currentRoom = _manager.GetRoom(room.RoomCode);
                if (currentRoom == null ||
                    currentRoom.State != LobbyRoomState.Starting ||
                    currentRoom.MatchId != matchId)
                {
                    await TryUpdateMatchStatusAsync(
                        matchId, MatchStatus.Aborted,
                        "Room was cancelled while the match was being persisted.", ct);
                    break;
                }

                room = currentRoom;
                Console.WriteLine($"[Lobby] Match {matchId} saved in API/MongoDB for room {room.RoomCode}");

                _ = MonitorStartingTimeoutAsync(room.RoomCode, matchId, ct);
                await BroadcastRoomAsync(room, ct);
                await BroadcastGameStartingAsync(room, request.RequestId, ct);
                break;
            }

            case "LEAVE_ROOM":
            {
                if (session.RoomCode == null)
                {
                    throw new InvalidOperationException("Not in a room.");
                }

                string code = session.RoomCode;

                var leaveResult = _manager.LeaveRoomAtomic(code, session.SessionId);

                var updatedRoom = leaveResult.Room;

                bool wasStarting = leaveResult.StartingCancelled;

                string? cancelledMatchId = leaveResult.CancelledMatchId;

                session.RoomCode = null;

                if (wasStarting && !string.IsNullOrWhiteSpace(cancelledMatchId))
                {
                    await TryUpdateMatchStatusAsync(
                        cancelledMatchId, MatchStatus.Aborted,
                        "A player left during Starting.", ct);
                }

                await connection.SendAsync(
                    LobbyResponse.Create(
                        "ROOM_LEFT",
                        new { roomCode = code },
                        request.RequestId), ct);

                if (wasStarting && updatedRoom != null)
                {
                    var failure = LobbyResponse.Create(
                        "START_FAILED",
                        new
                        {
                            roomCode = code,
                            reason =
                                "A player left during Starting."
                        });

                    foreach (var player in updatedRoom.Players)
                    {
                        if (_connections.TryGetValue(
                            player.PlayerId, out var other))
                        {
                            try
                            {
                                await other.SendAsync(failure, ct);
                            }
                            catch (IOException) { }
                            catch (ObjectDisposedException) { }
                        }
                    }
                }

                await BroadcastRoomAsync(updatedRoom, ct);
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
                matchId = room.MatchId,
                startedAt = room.StartedAt,
                roomVersion = room.RoomVersion,
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

    private async Task MonitorStartingTimeoutAsync(string roomCode, string matchId, CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), ct);

            var room = _manager.RollBackStarting(roomCode, matchId);

            if (room == null)
            {
                return;
            }

            Console.WriteLine($"[Lobby] Match {matchId} timed out.");

            await TryUpdateMatchStatusAsync(
                matchId, MatchStatus.Failed,
                "Game Server preparation timed out.", ct);

            var message = LobbyResponse.Create("START_FAILED", new
            {
                roomCode,
                matchId,
                reason = "Game Server preparation timed out."
            });

            foreach (var player in room.Players)
            {
                if (!_connections.TryGetValue(player.PlayerId, out var connection))
                {
                    continue;
                }

                try
                {
                    await connection.SendAsync(message, ct);
                }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
            }

            await BroadcastRoomAsync(room, ct);
        }
        catch (OperationCanceledException)
        {
            // Server shutting down.
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Lobby] Timeout monitor error: {ex.Message}");
        }
    }

    private async Task TryUpdateMatchStatusAsync(
        string matchId,
        MatchStatus status,
        string reason,
        CancellationToken ct)
    {
        try
        {
            await _matchApiClient.UpdateStatusAsync(
                matchId, status, reason, ct);
            Console.WriteLine($"[Lobby] Match {matchId} status updated to {status}.");
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Console.WriteLine(
                $"[Lobby] Could not update Match {matchId} to {status}: {ex.Message}");
        }
    }

    private async Task BroadcastStartFailedAsync(
        LobbyRoom room,
        string matchId,
        string reason,
        string? requestId,
        CancellationToken ct)
    {
        var response = LobbyResponse.Create(
            "START_FAILED",
            new { roomCode = room.RoomCode, matchId, reason },
            requestId);

        foreach (var player in room.Players)
        {
            if (!_connections.TryGetValue(player.PlayerId, out var playerConnection))
                continue;

            try
            {
                await playerConnection.SendAsync(response, ct);
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        }
    }

    private async Task BroadcastGameStartingAsync(LobbyRoom room, string? requestId, CancellationToken ct)
    {
        var response = LobbyResponse.Create(
            "GAME_STARTING",
            new
            {
                roomCode = room.RoomCode,
                matchId = room.MatchId,
                levelId = 1,
                state = room.State.ToString(),
                startedAt = room.StartedAt,

                // Endpoint for the Unity LiteNetLib client.
                gameServer = new
                {
                    host = _gameServerHost,
                    port = _gameServerPort,
                    transport = "UDP"
                },

                players = room.Players.Select(p => new
                {
                    playerId = p.PlayerId,
                    username = p.Username,
                    role = p.Role?.ToString(),
                    isHost = p.IsHost
                }).ToArray()
            },
            requestId
        );

        foreach (var player in room.Players)
        {
            if (!_connections.TryGetValue(
                player.PlayerId, out var connection))
            {
                Console.WriteLine($"[Lobby] Missing connection: {player.PlayerId}");
                continue;
            }

            try
            {
                await connection.SendAsync(response, ct);
            }
            catch (IOException)
            {
                Console.WriteLine($"[Lobby] Failed to notify: {player.PlayerId}");
            }
            catch (ObjectDisposedException)
            {
                Console.WriteLine($"[Lobby] Connection closed: {player.PlayerId}");
            }
            catch (OperationCanceledException)
            {
                if (ct.IsCancellationRequested) throw;
            }
        }
    }
}
