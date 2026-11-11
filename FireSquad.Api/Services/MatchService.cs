using FireSquad.Api.DTOs.Matches;
using FireSquad.Api.Models;
using FireSquad.Api.Repositories.Interfaces;
using FireSquad.Api.Services.Interfaces;
using FireSquad.Shared.DTOs.Matches;
using FireSquad.Shared.Enums;

namespace FireSquad.Api.Services;

public sealed class MatchService : IMatchService
{
    private readonly IMatchRepository _repository;

    public MatchService(IMatchRepository repository)
    {
        _repository = repository;
    }

    public async Task CreateAsync(CreateMatchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.MatchId))
            throw new ArgumentException("MatchId is required.");

        if (string.IsNullOrWhiteSpace(request.RoomCode))
            throw new ArgumentException("RoomCode is required.");

        if (string.IsNullOrWhiteSpace(request.HostUserId))
            throw new ArgumentException("HostUserId is required.");

        if (request.Players == null || request.Players.Count != 3)
            throw new ArgumentException("A FireSquad match requires exactly 3 players.");

        if (request.Players.Any(p =>
            string.IsNullOrWhiteSpace(p.PlayerId) ||
            string.IsNullOrWhiteSpace(p.UserId) ||
            string.IsNullOrWhiteSpace(p.Username) ||
            string.IsNullOrWhiteSpace(p.Role)))
        {
            throw new ArgumentException("Every player must have PlayerId, UserId, Username and Role.");
        }

        if (request.Players.Select(p => p.UserId).Distinct(StringComparer.Ordinal).Count() != 3)
            throw new ArgumentException("A match cannot contain the same user more than once.");

        if (request.Players.Select(p => p.Role).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 3)
            throw new ArgumentException("All players must have unique roles.");

        if (!request.Players.Any(p => p.UserId == request.HostUserId && p.IsHost))
            throw new ArgumentException("HostUserId must belong to the host player in the match.");

        var match = new Match
        {
            MatchId = request.MatchId,
            RoomCode = request.RoomCode,
            HostUserId = request.HostUserId,
            Players = request.Players.Select(p => new MatchPlayer
            {
                PlayerId = p.PlayerId,
                UserId = p.UserId,
                Username = p.Username,
                Role = p.Role,
                IsHost = p.IsHost
            }).ToList(),
            Status = MatchStatus.Starting.ToString(),
            CreatedAt = DateTime.UtcNow,
            StartingAt = request.StartingAt == default
                ? DateTime.UtcNow
                : request.StartingAt
        };

        await _repository.CreateIfNotExistsAsync(match);
    }

    public async Task<bool> UpdateStatusAsync(
        string matchId,
        UpdateMatchStatusRequest request)
    {
        if (string.IsNullOrWhiteSpace(matchId))
            throw new ArgumentException("MatchId is required.");

        if (request.Status is not MatchStatus.InProgress
            and not MatchStatus.Aborted
            and not MatchStatus.Failed)
        {
            throw new ArgumentException(
                "Only InProgress, Aborted or Failed can be set from Starting.");
        }

        return await _repository.UpdateStartingStatusAsync(
            matchId,
            request.Status.ToString(),
            request.FailureReason);
    }

    public async Task<bool> CompleteAsync(
        string matchId,
        CompleteMatchRequest request)
    {
        var match = await _repository.GetByIdAsync(matchId);
        if (match == null || match.Status != MatchStatus.InProgress.ToString())
            return false;

        var endedAt = DateTime.UtcNow;
        var startedAt = match.StartedAt ?? match.StartingAt;
        var duration = Math.Max(0L, (long)(endedAt - startedAt).TotalSeconds);

        var result = new MatchResult
        {
            WinnerTeam = request.WinnerTeam,
            Summary = request.Summary,
            Scores = request.Scores ?? new Dictionary<string, int>()
        };

        return await _repository.CompleteAsync(
            matchId, result, endedAt, duration);
    }

    public async Task<List<MatchHistoryItemResponse>> GetHistoryAsync(
        string userId,
        int limit)
    {
        var matches = await _repository.GetHistoryByUserIdAsync(userId, limit);

        return matches.Select(match => new MatchHistoryItemResponse
        {
            MatchId = match.MatchId,
            RoomCode = match.RoomCode,
            Status = match.Status,
            CreatedAt = match.CreatedAt,
            StartingAt = match.StartingAt,
            StartedAt = match.StartedAt,
            EndedAt = match.EndedAt,
            DurationSeconds = match.DurationSeconds,
            Players = match.Players.Select(player => new MatchPlayerResponse
            {
                UserId = player.UserId,
                Username = player.Username,
                Role = player.Role,
                IsHost = player.IsHost
            }).ToList(),
            Result = match.Result == null ? null : new MatchResultResponse
            {
                WinnerTeam = match.Result.WinnerTeam,
                Summary = match.Result.Summary,
                Scores = match.Result.Scores
            }
        }).ToList();
    }
}