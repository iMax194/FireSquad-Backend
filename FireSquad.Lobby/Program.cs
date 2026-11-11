using FireSquad.Lobby.Manager;
using FireSquad.Lobby.Models;
using FireSquad.Lobby.Networking;
using FireSquad.Lobby.Security;
using FireSquad.Lobby.Services;

Console.Title = "FireSquad Lobby Server";

if (args.Contains("--test-manager"))
{
    TestLobbyManager();
    return;
}

using var cancellationTokenSource = new CancellationTokenSource();

Console.CancelKeyPress += (sender, e) =>
{
    e.Cancel = true;
    cancellationTokenSource.Cancel();
};


var jwtKey = Environment.GetEnvironmentVariable(
    "Jwt__Key");

if (string.IsNullOrWhiteSpace(jwtKey))
{
    throw new InvalidOperationException(
        "Jwt__Key environment variable is missing.");
}

var jwtValidator = new JwtValidator(
    jwtKey,
    issuer: "FireSquad.Api",
    audience: "FireSquad.Unity");

var apiBaseUrl = Environment.GetEnvironmentVariable("Api__BaseUrl")
    ?? "http://localhost:5000/";

var internalApiKey = Environment.GetEnvironmentVariable(
    "InternalApi__ServiceKey");

// Unity clients receive this reachable endpoint in GAME_STARTING.
var gameServerHost = Environment.GetEnvironmentVariable("GameServer__Host")
    ?? "127.0.0.1";

var gameServerPort = 9050;
var configuredGameServerPort = Environment.GetEnvironmentVariable("GameServer__Port");
if (!string.IsNullOrWhiteSpace(configuredGameServerPort) &&
    (!int.TryParse(configuredGameServerPort, out gameServerPort) ||
     gameServerPort < 1 || gameServerPort > 65535))
{
    throw new InvalidOperationException(
        "GameServer__Port must be a valid port number between 1 and 65535.");
}

if (string.IsNullOrWhiteSpace(gameServerHost))
{
    throw new InvalidOperationException(
        "GameServer__Host must contain a reachable host or IP address.");
}

if (string.IsNullOrWhiteSpace(internalApiKey))
{
    throw new InvalidOperationException(
        "InternalApi__ServiceKey environment variable is missing. " +
        "Use the same service key when starting FireSquad.Api and FireSquad.Lobby.");
}

using var matchApiClient = new MatchApiClient(apiBaseUrl, internalApiKey);
var lobbyServer = new LobbyTcpServer(7777, jwtValidator, matchApiClient, gameServerHost, gameServerPort);


Console.WriteLine("================================");
Console.WriteLine("     FIRESQUAD LOBBY SERVER     ");
Console.WriteLine("================================");

await lobbyServer.StartAsync(cancellationTokenSource.Token);

static void TestLobbyManager()
{
    var manager = new LobbyManager();

    Console.WriteLine("=== Lobby Manager Test ===");

    var room = manager.CreateRoom("player1", "Firefighter01");

    Console.WriteLine($"Created room: {room.RoomCode}");

    var code = room.RoomCode;

    manager.JoinRoom(code, "player2", "Player02");
    manager.JoinRoom(code, "player3", "Player03");

    manager.SelectRole(code, "player1", PlayerRole.Scout);
    manager.SelectRole(code, "player2", PlayerRole.Foamer);
    manager.SelectRole(code, "player3", PlayerRole.Breaker);

    foreach (var id in new[]
    {
        "player1", "player2", "player3"
    })
    {
        manager.SetReady(code, id, true);
    }

    var result = manager.GetRoom(code)!;

    Console.WriteLine($"Players: {result.Players.Count}/3");

    foreach (var player in result.Players)
    {
        Console.WriteLine($"{player.Username} | {player.Role} | Ready={player.IsReady} | Host={player.IsHost}");
    }

    Console.WriteLine($"Can Start: {manager.CanStart(code, "player1")}");

    Console.WriteLine($"Non-host Can Start: {manager.CanStart(code, "player2")}");

    try
    {
        manager.SelectRole(code, "player2", PlayerRole.Scout);

        throw new Exception("Duplicate role was incorrectly accepted.");
    }
    catch (InvalidOperationException)
    {
        Console.WriteLine("Duplicate role: Rejected correctly");
    }

    var updatedRoom =
        manager.GetRoom(code)!;

    if (!manager.CanStart(code, "player1"))
        throw new Exception("Host should be allowed to start.");

    if (manager.CanStart(code, "player2"))
        throw new Exception("Non-host should not start.");

    if (updatedRoom.Players.Count != 3)
        throw new Exception("Player count is incorrect.");

    Console.WriteLine("All basic tests passed!");
}

Console.WriteLine("[Lobby] Server stopped.");