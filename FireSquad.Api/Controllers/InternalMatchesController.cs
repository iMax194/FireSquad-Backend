using System.Security.Cryptography;
using System.Text;
using FireSquad.Api.Services.Interfaces;
using FireSquad.Shared.DTOs.Matches;
using Microsoft.AspNetCore.Mvc;

namespace FireSquad.Api.Controllers;

[ApiController]
[Route("internal/matches")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class InternalMatchesController : ControllerBase
{
    private readonly IMatchService _matchService;
    private readonly IConfiguration _configuration;

    public InternalMatchesController(
        IMatchService matchService,
        IConfiguration configuration)
    {
        _matchService = matchService;
        _configuration = configuration;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateMatchRequest request)
    {
        if (!HasValidServiceKey())
            return Unauthorized();

        try
        {
            await _matchService.CreateAsync(request);
            return Ok(new { matchId = request.MatchId, status = "Starting" });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPatch("{matchId}/status")]
    public async Task<IActionResult> UpdateStatus(
        string matchId,
        [FromBody] UpdateMatchStatusRequest request)
    {
        if (!HasValidServiceKey())
            return Unauthorized();

        try
        {
            var updated = await _matchService.UpdateStatusAsync(matchId, request);
            if (!updated)
                return Conflict(new { message = "Match was not found or is no longer in Starting state." });

            return Ok(new { matchId, status = request.Status.ToString() });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{matchId}/complete")]
    public async Task<IActionResult> Complete(
        string matchId,
        [FromBody] CompleteMatchRequest request)
    {
        if (!HasValidServiceKey())
            return Unauthorized();

        var completed = await _matchService.CompleteAsync(matchId, request);
        if (!completed)
            return Conflict(new { message = "Match was not found or is not InProgress." });

        return Ok(new { matchId, status = "Completed" });
    }

    private bool HasValidServiceKey()
    {
        var expected = _configuration["InternalApi:ServiceKey"];
        var supplied = Request.Headers["X-Service-Key"].ToString();

        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(supplied))
            return false;

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);

        return expectedBytes.Length == suppliedBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }
}
