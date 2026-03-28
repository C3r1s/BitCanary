using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Messenger.Infrastructure.Persistence;

/// <summary>
/// Used by the EF Core CLI tooling when there is no running host to resolve the DbContext.
///
/// Usage (from the solution root):
///   dotnet ef migrations add InitialCreate \
///     --project src/Backend/Messenger.Infrastructure \
///     --startup-project src/Backend/Messenger.Api \
///     --output-dir Persistence/Migrations
///
/// The factory reads the connection string from the environment variable
/// MESSENGER_DB (or falls back to the standard local dev default).
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("MESSENGER_DB")
            ?? "Host=localhost;Port=5432;Database=canary_avo;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new AppDbContext(optionsBuilder.Options);
    }
}
