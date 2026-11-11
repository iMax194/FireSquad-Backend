using FireSquad.Api.DTOs.Matches;
using FireSquad.Shared.DTOs.Matches;
using FireSquad.Shared.Enums;

namespace FireSquad.Api.Services.Interfaces;

public interface IMatchService
{
    Task CreateAsync(CreateMatchRequest request);
    Task<bool> UpdateStatusAsync(string matchId, UpdateMatchStatusRequest request);
    Task<bool> CompleteAsync(string matchId, CompleteMatchRequest request);
    Task<List<MatchHistoryItemResponse>> GetHistoryAsync(string userId, int limit);
}
