using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Shortener.Infrastructure;

public sealed class ShortenerDbContextFactory : IDesignTimeDbContextFactory<ShortenerDbContext>
{
    public ShortenerDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Shortener")
            ?? "Data Source=linkfoundry-design.db";
        var options = new DbContextOptionsBuilder<ShortenerDbContext>()
            .UseSqlite(connectionString, sqlite => sqlite.MigrationsAssembly(typeof(ShortenerDbContext).Assembly.FullName))
            .Options;
        return new ShortenerDbContext(options);
    }
}