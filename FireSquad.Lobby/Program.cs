using FireSquad.Lobby.Manager;
using FireSquad.Lobby.Models;
using FireSquad.Lobby.Networking;

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

var lobbyServer = new LobbyTcpServer(7777);

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
    manager.JoinRoom(code, "player4", "Player04");

    manager.SelectRole(code, "player1", PlayerRole.Scout);
    manager.SelectRole(code, "player2", PlayerRole.Foamer);
    manager.SelectRole(code, "player3", PlayerRole.Breaker);
    manager.SelectRole(code, "player4", PlayerRole.Hoser);

    foreach (var id in new[]
    {
        "player1", "player2", "player3", "player4"
    })
    {
        manager.SetReady(code, id, true);
    }

    var result = manager.GetRoom(code)!;

    Console.WriteLine($"Players: {result.Players.Count}/4");

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

    if (updatedRoom.Players.Count != 4)
        throw new Exception("Player count is incorrect.");

    Console.WriteLine("All basic tests passed!");
}

Console.WriteLine("[Lobby] Server stopped.");