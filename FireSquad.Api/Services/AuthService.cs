
using FireSquad.Api.DTOs.Auth;
using FireSquad.Api.Models;
using FireSquad.Api.Repositories.Interfaces;
using FireSquad.Api.Services.Interfaces;
using Microsoft.AspNetCore.Identity;

namespace FireSquad.Api.Services;

public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly JwtService _jwtService;

    public AuthService(
        IUserRepository userRepository,
        IPasswordHasher<User> passwordHasher,
        JwtService jwtService)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _jwtService = jwtService;
    }

    public async Task<RegisterResponse> RegisterAsync(
        RegisterRequest request)
    {
        var username = request.Username.Trim();
        var email = request.Email.Trim().ToLowerInvariant();

        // Validate normalized input
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new ArgumentException(
                "Username cannot be empty.");
        }

        if (await _userRepository.UsernameExistsAsync(username))
        {
            throw new InvalidOperationException(
                "Username already exists.");
        }

        if (await _userRepository.EmailExistsAsync(email))
        {
            throw new InvalidOperationException(
                "Email already exists.");
        }

        // Create user object
        var user = new User
        {
            Username = username,
            Email = email,
            CreatedAt = DateTime.UtcNow
        };

        // Hash password, never store plain text password
        user.PasswordHash = _passwordHasher.HashPassword(
            user, request.Password);

        // Save to MongoDB
        await _userRepository.CreateAsync(user);

        return new RegisterResponse
        {
            Message = "Registration successful",
            UserId = user.Id ?? string.Empty,
            Username = user.Username,
            Email = user.Email
        };
    }

    public async Task<LoginResponse?> LoginAsync(
        LoginRequest request)
    {
        var username = request.Username.Trim();

        var user = await _userRepository
            .GetByUsernameAsync(username);

        if (user == null)
        {
            return null;
        }

        var verificationResult = _passwordHasher
            .VerifyHashedPassword(
                user,
                user.PasswordHash,
                request.Password);

        if (verificationResult ==
            PasswordVerificationResult.Failed)
        {
            return null;
        }

        var (token, expiresAt) =
            _jwtService.GenerateToken(user);

        return new LoginResponse
        {
            Message = "Login successful",
            AccessToken = token,
            TokenType = "Bearer",
            ExpiresAt = expiresAt,
            UserId = user.Id ?? string.Empty,
            Username = user.Username
        };
    }
}
