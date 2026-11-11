using System.Security.Claims;
using FireSquad.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FireSquad.Api.Controllers;

[ApiController]
[Route("api/matches")]
[Authorize]
public sealed class MatchesController : ControllerBase
{
    private readonly IMatchService _matchService;

    public MatchesController(IMatchService matchService)
    {
        _matchService = matchService;
    }

    [HttpGet("history")]
    public async Task<IActionResult> GetMyHistory([FromQuery] int limit = 20)
    {
        var userId = User.FindFirst("sub")?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();

        limit = Math.Clamp(limit, 1, 100);
        var history = await _matchService.GetHistoryAsync(userId, limit);
        return Ok(history);
    }
}
