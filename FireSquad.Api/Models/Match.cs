using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace FireSquad.Api.Models;

public sealed class Match
{
    [BsonId]
    public string MatchId { get; set; } = string.Empty;

    [BsonElement("roomCode")]
    public string RoomCode { get; set; } = string.Empty;

    [BsonElement("hostUserId")]
    public string HostUserId { get; set; } = string.Empty;

    [BsonElement("players")]
    public List<MatchPlayer> Players { get; set; } = new();

    [BsonElement("status")]
    public string Status { get; set; } = "Starting";

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("startingAt")]
    public DateTime StartingAt { get; set; }

    [BsonElement("startedAt")]
    [BsonIgnoreIfNull]
    public DateTime? StartedAt { get; set; }

    [BsonElement("endedAt")]
    [BsonIgnoreIfNull]
    public DateTime? EndedAt { get; set; }

    [BsonElement("durationSeconds")]
    [BsonIgnoreIfNull]
    public long? DurationSeconds { get; set; }

    [BsonElement("failureReason")]
    [BsonIgnoreIfNull]
    public string? FailureReason { get; set; }

    [BsonElement("result")]
    [BsonIgnoreIfNull]
    public MatchResult? Result { get; set; }
}

public sealed class MatchPlayer
{
    [BsonElement("playerId")]
    public string PlayerId { get; set; } = string.Empty;

    [BsonElement("userId")]
    public string UserId { get; set; } = string.Empty;

    [BsonElement("username")]
    public string Username { get; set; } = string.Empty;

    [BsonElement("role")]
    public string Role { get; set; } = string.Empty;

    [BsonElement("isHost")]
    public bool IsHost { get; set; }
}

public sealed class MatchResult
{
    [BsonElement("winnerTeam")]
    [BsonIgnoreIfNull]
    public string? WinnerTeam { get; set; }

    [BsonElement("summary")]
    [BsonIgnoreIfNull]
    public string? Summary { get; set; }

    [BsonElement("scores")]
    public Dictionary<string, int> Scores { get; set; } = new();
}
