using FireSquad.Api.Models;

namespace FireSquad.Api.Repositories.Interfaces;

public interface IMatchRepository
{
    Task<Match> CreateIfNotExistsAsync(Match match);
    Task<Match?> GetByIdAsync(string matchId);
    Task<bool> UpdateStartingStatusAsync(
        string matchId,
        string status,
        string? failureReason);
    Task<bool> CompleteAsync(
        string matchId,
        MatchResult result,
        DateTime endedAt,
        long durationSeconds);
    Task<List<Match>> GetHistoryByUserIdAsync(string userId, int limit);
}
