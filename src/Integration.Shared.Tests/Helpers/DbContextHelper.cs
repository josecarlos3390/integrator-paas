using Integration.Shared.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Integration.Shared.Tests.Helpers;

public static class DbContextHelper
{
    public static IntegrationDbContext CreateInMemoryContext(string? databaseName = null)
    {
        databaseName ??= Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<IntegrationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        var context = new IntegrationDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }
}
