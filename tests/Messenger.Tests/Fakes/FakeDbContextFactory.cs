// Автотест BitCanary: проверка «FakeDbContextFactory».
using Messenger.Application.Abstractions;
using Messenger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Tests.Fakes;

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
