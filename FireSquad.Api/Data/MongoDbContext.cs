
using MongoDB.Bson;
using MongoDB.Driver;

namespace FireSquad.Api.Data;

using FireSquad.Api.Models;

public class MongoDbContext
{
    private readonly IMongoDatabase _database;

    public IMongoCollection<User> Users =>
    _database.GetCollection<User>("users");

    public MongoDbContext(IConfiguration configuration)
    {
        var connectionString =
            configuration["MongoDb:ConnectionString"]
            ?? throw new InvalidOperationException(
                "Missing MongoDB connection string.");

        var databaseName =
            configuration["MongoDb:DatabaseName"]
            ?? "firesquad_db";

        var client = new MongoClient(connectionString);

        _database = client.GetDatabase(databaseName);
    }

    public IMongoDatabase Database => _database;

    public IMongoCollection<BsonDocument> ConnectionTests =>
        _database.GetCollection<BsonDocument>(
            "connection_tests");
}
