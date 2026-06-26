using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CardManagement.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for EF Core migrations.
/// Used by `dotnet ef` CLI tool to create the DbContext without running the application host.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<CardManagementDbContext>
{
    public CardManagementDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<CardManagementDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Database=cardmanagement_design;Username=postgres;Password=postgres");

        return new CardManagementDbContext(optionsBuilder.Options);
    }
}
