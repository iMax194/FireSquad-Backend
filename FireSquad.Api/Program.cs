
using FireSquad.Api.Data;
using MongoDB.Bson;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<MongoDbContext>();

var app = builder.Build();

app.MapGet("/", () =>
    Results.Ok(new
    {
        message = "FireSquad API is running!",
        status = "OK"
    }));

// Test routes only available in Development.
if (app.Environment.IsDevelopment())
{
    app.MapGet("/api/test/mongodb", async (
        MongoDbContext db) =>
    {
        try
        {
            await db.Database.RunCommandAsync<BsonDocument>(
                new BsonDocument("ping", 1));

            return Results.Ok(new
            {
                message = "MongoDB connected successfully!"
            });
        }
        catch (Exception)
        {
            return Results.Problem(
                "MongoDB connection failed.",
                statusCode: 503);
        }
    });

    app.MapPost("/api/test/insert", async (
    MongoDbContext db) =>
    {
        var document = new BsonDocument
        {
            { "project", "FireSquad" },
            { "status", "Connected" },
            { "createdAt", DateTime.UtcNow }
        };

        await db.ConnectionTests.InsertOneAsync(document);

        return Results.Created(
            "/api/test/data",
            new
            {
                message = "Inserted successfully!"
            });
    });

    app.MapGet("/api/test/data", async (
    MongoDbContext db) =>
    {
        var documents = await db.ConnectionTests
            .Find(FilterDefinition<BsonDocument>.Empty)
            .ToListAsync();

        var result = documents.Select(d => new
        {
            id = d["_id"].ToString(),
            project = d.GetValue(
                "project", BsonNull.Value).ToString(),
            status = d.GetValue(
                "status", BsonNull.Value).ToString()
        }).ToList();

        return Results.Ok(result);
    });
}

app.Run();
