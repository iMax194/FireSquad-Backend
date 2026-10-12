
using FireSquad.Api.DTOs.Auth;

namespace FireSquad.Api.Services.Interfaces;

public interface IAuthService
{
    Task<RegisterResponse> RegisterAsync(
        RegisterRequest request);

    Task<LoginResponse?> LoginAsync(
        LoginRequest request);
}
