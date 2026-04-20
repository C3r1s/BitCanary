using Messenger.Application.Abstractions;
using Messenger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Tests.Fakes;

/// <summary>
/// Per D-04: creates a fresh IAppDbContext backed by EF InMemory.
/// Each call uses Guid.NewGuid().ToString() as database name — full isolation.
/// </summary>
public static class FakeDbContextFactory
{
    public static IAppDbContext Create()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }
}
