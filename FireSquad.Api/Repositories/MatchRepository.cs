using FireSquad.Api.Data;
using FireSquad.Api.Models;
using FireSquad.Api.Repositories.Interfaces;
using MongoDB.Driver;

namespace FireSquad.Api.Repositories;

public sealed class MatchRepository : IMatchRepository
{
    private readonly IMongoCollection<Match> _matches;

    public MatchRepository(MongoDbContext context)
    {
        _matches = context.Matches;
    }

    public async Task<Match> CreateIfNotExistsAsync(Match match)
    {
        var existing = await GetByIdAsync(match.MatchId);
        if (existing != null)
            return existing;

        try
        {
            await _matches.InsertOneAsync(match);
            return match;
        }
        catch (MongoWriteException ex) when (
            ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // Idempotent retry: return the document created by the first request.
            existing = await GetByIdAsync(match.MatchId);
            if (existing != null)
                return existing;

            throw;
        }
    }

    public async Task<Match?> GetByIdAsync(string matchId)
    {
        return await _matches.Find(x => x.MatchId == matchId).FirstOrDefaultAsync();
    }

    public async Task<bool> UpdateStartingStatusAsync(
        string matchId,
        string status,
        string? failureReason)
    {
        var update = Builders<Match>.Update
            .Set(x => x.Status, status)
            .Set(x => x.FailureReason, failureReason);

        if (status == "InProgress")
            update = update.Set(x => x.StartedAt, DateTime.UtcNow);

        if (status is "Failed" or "Aborted")
            update = update.Set(x => x.EndedAt, DateTime.UtcNow);

        var result = await _matches.UpdateOneAsync(
            x => x.MatchId == matchId && x.Status == "Starting",
            update);

        return result.MatchedCount > 0;
    }

    public async Task<bool> CompleteAsync(
        string matchId,
        MatchResult result,
        DateTime endedAt,
        long durationSeconds)
    {
        var update = Builders<Match>.Update
            .Set(x => x.Status, "Completed")
            .Set(x => x.Result, result)
            .Set(x => x.EndedAt, endedAt)
            .Set(x => x.DurationSeconds, durationSeconds)
            .Unset(x => x.FailureReason);

        var writeResult = await _matches.UpdateOneAsync(
            x => x.MatchId == matchId && x.Status == "InProgress",
            update);

        return writeResult.MatchedCount > 0;
    }

    public Task<List<Match>> GetHistoryByUserIdAsync(string userId, int limit)
    {
        return _matches
            .Find(x => x.Players.Any(player => player.UserId == userId))
            .SortByDescending(x => x.CreatedAt)
            .Limit(limit)
            .ToListAsync();
    }
}
