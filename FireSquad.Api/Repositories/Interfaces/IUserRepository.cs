
using FireSquad.Api.Models;

namespace FireSquad.Api.Repositories.Interfaces;

public interface IUserRepository
{
    // Find user by ID
    Task<User?> GetByIdAsync(string id);

    // Find user by username
    Task<User?> GetByUsernameAsync(string username);

    // Find user by email
    Task<User?> GetByEmailAsync(string email);

    // Check if username already exists
    Task<bool> UsernameExistsAsync(string username);

    // Check if email already exists
    Task<bool> EmailExistsAsync(string email);

    // Create a new user
    Task CreateAsync(User user);
}
