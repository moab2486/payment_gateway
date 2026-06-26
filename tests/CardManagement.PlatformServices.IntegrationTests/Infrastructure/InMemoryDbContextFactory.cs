using CardManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CardManagement.PlatformServices.IntegrationTests.Infrastructure;

/// <summary>
/// Creates in-memory EF Core DbContext instances for integration testing.
/// Each test gets an isolated database to prevent cross-test interference.
/// </summary>
public static class InMemoryDbContextFactory
{
    /// <summary>
    /// Creates a new CardManagementDbContext backed by an in-memory SQLite database.
    /// SQLite in-memory provides better relational behavior than EF InMemory provider.
    /// </summary>
    public static CardManagementDbContext Create(string? databaseName = null)
    {
        databaseName ??= $"TestDb_{Guid.NewGuid():N}";

        var options = new DbContextOptionsBuilder<CardManagementDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        var context = new CardManagementDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }
}
