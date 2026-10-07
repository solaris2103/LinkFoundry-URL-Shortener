using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Shortener.Core;
using Shortener.Infrastructure;

namespace Shortener.Tests;

public sealed class ShortLinkServiceTests
{
    [Fact]
    public async Task CreateAsync_rejects_non_http_destination()
    {
        await using var database = CreateDatabase();
        var service = CreateService(database);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync(new CreateLinkRequest("javascript:alert(1)", null, null), CancellationToken.None));

        Assert.Contains("HTTP or HTTPS", exception.Message);
    }

    [Theory]
    [InlineData("https://user:secret@example.com/path", null)]
    [InlineData("https://example.com/path", "health")]
    public async Task CreateAsync_rejects_credentials_and_reserved_codes(string destination, string? customCode)
    {
        await using var database = CreateDatabase();
        var service = CreateService(database);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(
            new CreateLinkRequest(destination, customCode, null), CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_rejects_duplicate_custom_code()
    {
        await using var database = CreateDatabase();
        var service = CreateService(database);
        await service.CreateAsync(new CreateLinkRequest("https://example.com", "release", null), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new CreateLinkRequest("https://other.example", "release", null), CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_reuses_expired_custom_code_and_resets_old_analytics()
    {
        await using var database = CreateDatabase();
        var service = CreateService(database);
        var original = await service.CreateAsync(new CreateLinkRequest(
            "https://old.example/offer", "campaign", null), CancellationToken.None);
        await service.RecordClickAsync(original.Code, "https://newsletter.example/old", CancellationToken.None);

        var originalRow = await database.Links.SingleAsync(link => link.Code == original.Code);
        originalRow.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await database.SaveChangesAsync();

        var reused = await service.CreateAsync(new CreateLinkRequest(
            "https://new.example/offer", "campaign", null), CancellationToken.None);
        var analytics = await service.GetAnalyticsAsync("campaign", CancellationToken.None);

        Assert.Equal(original.Code, reused.Code);
        Assert.Equal("https://new.example/offer", reused.DestinationUrl);
        Assert.True(reused.IsActive);
        Assert.Equal(0, reused.ClickCount);
        Assert.NotNull(analytics);
        Assert.Equal(0, analytics.Link.ClickCount);
        Assert.Empty(analytics.DailyClicks);
        Assert.Empty(analytics.TopReferrers);
        Assert.Equal("https://new.example/offer", await service.RecordClickAsync("campaign", null, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_generates_twelve_character_codes()
    {
        await using var database = CreateDatabase();
        var service = CreateService(database);

        var created = await service.CreateAsync(new CreateLinkRequest("https://example.com", null, null), CancellationToken.None);

        Assert.Matches("^[A-Za-z0-9]{12}$", created.Code);
    }

    [Fact]
    public async Task Redirect_records_click_and_analytics_groups_by_referrer()
    {
        await using var database = CreateDatabase();
        var service = CreateService(database);
        var created = await service.CreateAsync(new CreateLinkRequest("https://example.com/path", "launch", null), CancellationToken.None);

        var destination = await service.RecordClickAsync(created.Code, "https://newsletter.example/campaign", CancellationToken.None);
        var analytics = await service.GetAnalyticsAsync(created.Code, CancellationToken.None);

        Assert.Equal("https://example.com/path", destination);
        Assert.NotNull(analytics);
        Assert.Equal(1, analytics.Link.ClickCount);
        Assert.Single(analytics.DailyClicks);
        Assert.Equal(new ReferrerCount("newsletter.example", 1), Assert.Single(analytics.TopReferrers));
    }

    [Fact]
    public async Task Redirect_does_not_resolve_expired_or_deactivated_links()
    {
        await using var database = CreateDatabase();
        var service = CreateService(database);
        var expired = await service.CreateAsync(new CreateLinkRequest(
            "https://example.com", "expired", DateTimeOffset.UtcNow.AddMinutes(1)), CancellationToken.None);
        var link = await database.Links.SingleAsync(item => item.Code == expired.Code);
        link.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await database.SaveChangesAsync();
        var expiredSummary = Assert.Single(await service.ListAsync(CancellationToken.None));
        var expiredAnalytics = await service.GetAnalyticsAsync(expired.Code, CancellationToken.None);

        var active = await service.CreateAsync(new CreateLinkRequest("https://example.com", "inactive", null), CancellationToken.None);
        await service.DeactivateAsync(active.Code, CancellationToken.None);

        Assert.False(expiredSummary.IsActive);
        Assert.NotNull(expiredAnalytics);
        Assert.False(expiredAnalytics.Link.IsActive);
        Assert.Null(await service.RecordClickAsync(expired.Code, null, CancellationToken.None));
        Assert.Null(await service.RecordClickAsync(active.Code, null, CancellationToken.None));
    }

    private static ShortLinkService CreateService(ShortenerDbContext? database = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["PublicBaseUrl"] = "http://localhost:5080" })
            .Build();
        return new ShortLinkService(database ?? CreateDatabase(), configuration);
    }

    private static ShortenerDbContext CreateDatabase()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<ShortenerDbContext>()
            .UseSqlite(connection)
            .Options;
        var database = new ShortenerDbContext(options);
        database.Database.EnsureCreated();
        return database;
    }
}