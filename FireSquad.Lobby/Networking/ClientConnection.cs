
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FireSquad.Lobby.Messages;
using FireSquad.Lobby.Models;

namespace FireSquad.Lobby.Networking;

public sealed class ClientConnection : IDisposable
{
    private readonly TcpClient _client;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            Converters =
            {
                new JsonStringEnumConverter()
            }
        };

    public PlayerSession Session { get; }

    public ClientConnection(TcpClient client)
    {
        _client = client;
        Session = new PlayerSession(client);

        var stream = client.GetStream();

        _reader = new StreamReader(
            stream, Encoding.UTF8, leaveOpen: true);

        _writer = new StreamWriter(
            stream, new UTF8Encoding(false),
            leaveOpen: true)
        {
            AutoFlush = true
        };
    }

    // Read at most 16 KiB per JSON message.
    public async Task<string?> ReadLineAsync(
        CancellationToken ct)
    {
        const int maxLength = 16 * 1024;
        var result = new StringBuilder();
        var buffer = new char[1];

        while (true)
        {
            int count = await _reader.ReadAsync(
                buffer.AsMemory(0, 1), ct);

            if (count == 0)
                return result.Length == 0
                    ? null
                    : throw new IOException(
                        "Incomplete JSON message.");

            if (buffer[0] == '\n')
                return result.ToString().TrimEnd('\r');

            result.Append(buffer[0]);

            if (result.Length > maxLength)
                throw new IOException(
                    "JSON message too large.");
        }
    }

    public async Task SendAsync(
        LobbyResponse response,
        CancellationToken ct = default)
    {
        string json = JsonSerializer.Serialize(
            response, JsonOptions);

        await _sendLock.WaitAsync(ct);

        try
        {
            await _writer.WriteLineAsync(
                json.AsMemory(), ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public void Dispose()
    {
        _client.Close();
        _reader.Dispose();
        _writer.Dispose();
        _sendLock.Dispose();
    }
}
