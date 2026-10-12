
using FireSquad.Api.Data;
using FireSquad.Api.Models;
using FireSquad.Api.Repositories.Interfaces;
using MongoDB.Driver;

namespace FireSquad.Api.Repositories;

public class UserRepository : IUserRepository
{
    private readonly IMongoCollection<User> _users;

    public UserRepository(MongoDbContext context)
    {
        _users = context.Users;
    }

    public async Task<User?> GetByIdAsync(string id)
    {
        return await _users
            .Find(user => user.Id == id)
            .FirstOrDefaultAsync();
    }

    public async Task<User?> GetByUsernameAsync(
        string username)
    {
        return await _users
            .Find(user => user.Username == username)
            .FirstOrDefaultAsync();
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        return await _users
            .Find(user => user.Email == email)
            .FirstOrDefaultAsync();
    }

    public async Task<bool> UsernameExistsAsync(
        string username)
    {
        return await _users
            .Find(user => user.Username == username)
            .AnyAsync();
    }

    public async Task<bool> EmailExistsAsync(string email)
    {
        return await _users
            .Find(user => user.Email == email)
            .AnyAsync();
    }

    public async Task CreateAsync(User user)
    {
        await _users.InsertOneAsync(user);
    }
}
