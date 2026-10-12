
namespace FireSquad.Api.DTOs.Auth;

public class LoginResponse
{
    public string Message { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";
    public DateTime ExpiresAt { get; set; }

    public string UserId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
}
