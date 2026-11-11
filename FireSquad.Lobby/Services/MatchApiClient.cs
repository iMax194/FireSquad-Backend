using System.Net.Http.Json;
using FireSquad.Shared.DTOs.Matches;
using FireSquad.Shared.Enums;

namespace FireSquad.Lobby.Services;

public sealed class MatchApiClient : IDisposable
{
    private readonly HttpClient _httpClient;

    public MatchApiClient(string baseUrl, string serviceKey)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new ArgumentException("API base URL is required.", nameof(baseUrl));

        if (string.IsNullOrWhiteSpace(serviceKey))
            throw new ArgumentException("Internal API service key is required.", nameof(serviceKey));

        var normalizedBaseUrl = baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/";
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(normalizedBaseUrl, UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(8)
        };
        _httpClient.DefaultRequestHeaders.Add("X-Service-Key", serviceKey);
    }

    public async Task CreateMatchAsync(
        CreateMatchRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "internal/matches", request, cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task UpdateStatusAsync(
        string matchId,
        MatchStatus status,
        string? failureReason,
        CancellationToken cancellationToken)
    {
        var request = new UpdateMatchStatusRequest
        {
            Status = status,
            FailureReason = failureReason
        };

        using var message = new HttpRequestMessage(
            HttpMethod.Patch,
            $"internal/matches/{Uri.EscapeDataString(matchId)}/status")
        {
            Content = JsonContent.Create(request)
        };

        using var response = await _httpClient.SendAsync(
            message, cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task CompleteMatchAsync(
        string matchId,
        CompleteMatchRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            $"internal/matches/{Uri.EscapeDataString(matchId)}/complete",
            request,
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(
            $"Match API returned {(int)response.StatusCode} ({response.StatusCode}). {body}",
            null,
            response.StatusCode);
    }

    public void Dispose() => _httpClient.Dispose();
}
