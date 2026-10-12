
using FireSquad.Api.Repositories.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FireSquad.Api.Controllers;

[ApiController]
[Route("api/test/users")]
public class UserRepositoryTestController : ControllerBase
{
    private readonly IUserRepository _userRepository;
    private readonly IWebHostEnvironment _environment;

    public UserRepositoryTestController(
        IUserRepository userRepository,
        IWebHostEnvironment environment)
    {
        _userRepository = userRepository;
        _environment = environment;
    }

    [HttpGet("check-username")]
    public async Task<IActionResult> CheckUsername(
        [FromQuery] string username)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            return BadRequest(new
            {
                message = "Username is required"
            });
        }

        var exists = await _userRepository
            .UsernameExistsAsync(username);

        return Ok(new
        {
            username,
            exists
        });
    }
}
