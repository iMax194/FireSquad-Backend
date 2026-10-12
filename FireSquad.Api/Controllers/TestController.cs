
using Microsoft.AspNetCore.Mvc;

namespace FireSquad.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TestController : ControllerBase
{
    [HttpGet("hello")]
    public IActionResult Hello()
    {
        return Ok(new
        {
            message = "FireSquad Controller is working!",
            architecture = "Controller-Service-Repository"
        });
    }
}
